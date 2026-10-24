/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 13:20:00 +08:00 中国标准时间 星期五
 * 描述：打印模板服务——CRUD + Scriban 渲染（2A 冻结契约 IPrintTemplateService 实现）+ 模板缓存
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Printing;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Printing;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Options;

namespace Qlx.Modules.Support.Services.Printing;

/// <summary>
/// 打印模板服务。同实例承载 2A 冻结契约 <see cref="IPrintTemplateService"/>
/// 与模块级管理契约 <see cref="ISupportPrintTemplateService"/>。
/// <para>渲染管线：启用态模板读穿缓存 → 公共组件 ScribanHtmlTemplateRenderer
/// （qrcode 占位替换 + Scriban 数据占位渲染）→ CSS 并入。模板占位符大小写敏感，与模型字段名 / 字典键一致。</para>
/// </summary>
public sealed class PrintTemplateService(
    SupportTenantDbContext db,
    ICacheService cache,
    IOptions<SupportPrintOptions> printOptions) : IPrintTemplateService, ISupportPrintTemplateService
{
    /// <summary>缓存键业务段前缀（print:tpl:{模板编码}）。</summary>
    private const string CacheBusinessKeyPrefix = "print:tpl:";

    private readonly SupportTenantDbContext _db = db;
    private readonly ICacheService _cache = cache;
    private readonly SupportPrintOptions _options = printOptions.Value;

    // ============ 2A 冻结契约：渲染 / 定义查询 ============

    /// <inheritdoc />
    public async Task<string> RenderAsync(string templateCode, object model, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateCode);
        ArgumentNullException.ThrowIfNull(model);

        var view = await LoadEnabledTemplateAsync(templateCode, cancellationToken).ConfigureAwait(false);

        // ① 渲染（公共组件）：qrcode 占位替换 + Scriban 数据占位
        var rendered = await ScribanHtmlTemplateRenderer.RenderAsync(view.ContentHtml, model).ConfigureAwait(false);

        // ② 附加 CSS 并入头部
        return view.Css is { Length: > 0 } css
            ? $"<style>{css}</style>{rendered}"
            : rendered;
    }

    /// <inheritdoc />
    public Task<PrintTemplateInfo?> GetTemplateAsync(string templateCode, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateCode);

        return FindByCodeInternalAsync(templateCode, cancellationToken) is { } queryTask
            ? QueryTemplateInfoAsync(queryTask, cancellationToken)
            : Task.FromResult<PrintTemplateInfo?>(null);
    }

    // ============ 模块级管理契约：CRUD / 启停 ============

    /// <inheritdoc />
    public async Task<PrintTemplateView> CreateAsync(PrintTemplateCreateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var exists = await _db.PrintTemplates
            .AnyAsync(t => t.TemplateCode == request.TemplateCode, cancellationToken).ConfigureAwait(false);
        if (exists)
        {
            throw new ConflictException($"打印模板编码 {request.TemplateCode} 已存在。", "PRINT_TEMPLATE_DUPLICATED");
        }

        var now = DateTimeOffset.UtcNow;
        var template = new PrintTemplate(
            _db.CurrentTenantId, request.TemplateCode, request.Name, request.BizType,
            request.ContentHtml, request.Css, now);
        _db.PrintTemplates.Add(template);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await InvalidateCacheAsync(request.TemplateCode, cancellationToken).ConfigureAwait(false);
        return ToView(template);
    }

    /// <inheritdoc />
    public async Task<PrintTemplateView> UpdateAsync(long id, PrintTemplateUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var template = await _db.PrintTemplates
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("打印模板", id.ToString(System.Globalization.CultureInfo.InvariantCulture));

        template.UpdateContent(request.Name, request.BizType, request.ContentHtml, request.Css, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await InvalidateCacheAsync(template.TemplateCode, cancellationToken).ConfigureAwait(false);
        return ToView(template);
    }

    /// <inheritdoc />
    public async Task SetStatusAsync(long id, PrintTemplateStatus status, CancellationToken cancellationToken)
    {
        var template = await _db.PrintTemplates
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("打印模板", id.ToString(System.Globalization.CultureInfo.InvariantCulture));

        template.SetStatus(status, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await InvalidateCacheAsync(template.TemplateCode, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrintTemplateView>> ListAsync(CancellationToken cancellationToken)
    {
        var views = await _db.PrintTemplates
            .AsNoTracking()
            .OrderBy(static t => t.TemplateCode)
            .ThenBy(static t => t.Version)
            .Select(static t => new PrintTemplateView(
                t.Id, t.TemplateCode, t.Name, t.BizType, t.ContentHtml, t.Css,
                t.Status, t.Version, t.CreatedAt, t.UpdatedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return views;
    }

    /// <inheritdoc />
    public async Task<PrintTemplateView?> GetByCodeAsync(string templateCode, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateCode);

        var view = await _db.PrintTemplates
            .AsNoTracking()
            .Where(t => t.TemplateCode == templateCode)
            .OrderByDescending(static t => t.Version)
            .Select(ToViewExpression())
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return view;
    }

    // ============ 内部实现 ============

    /// <summary>读穿缓存加载启用态模板（渲染唯一承接状态，停用 / 草稿不承接渲染）。</summary>
    /// <param name="templateCode">模板编码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>模板视图。</returns>
    /// <exception cref="NotFoundException">模板不存在或未启用。</exception>
    private Task<PrintTemplateView> LoadEnabledTemplateAsync(string templateCode, CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(_db.CurrentTenantId, templateCode);
        return _cache.GetOrCreateAsync<PrintTemplateView>(
            cacheKey,
            async innerCt =>
            {
                var view = await _db.PrintTemplates
                    .AsNoTracking()
                    .Where(t => t.TemplateCode == templateCode && t.Status == PrintTemplateStatus.Enabled)
                    .OrderByDescending(static t => t.Version)
                    .Select(ToViewExpression())
                    .FirstOrDefaultAsync(innerCt).ConfigureAwait(false)
                    ?? throw new NotFoundException("启用态打印模板", templateCode);
                return view;
            },
            cancellationToken,
            new CacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.TemplateCacheMinutes),
            });
    }

    /// <summary>构建租户级模板缓存键（QlxCacheKey 规范归口）。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="templateCode">模板编码。</param>
    /// <returns>规范缓存键。</returns>
    private static string BuildCacheKey(long tenantId, string templateCode)
        => QlxCacheKey.BuildTenantKey(tenantId, QlxModuleCodes.Support, $"{CacheBusinessKeyPrefix}{templateCode}");

    /// <summary>失效指定模板缓存（CRUD / 启停后调用，读穿自愈）。</summary>
    /// <param name="templateCode">模板编码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否实际移除。</returns>
    private Task<bool> InvalidateCacheAsync(string templateCode, CancellationToken cancellationToken)
        => _cache.RemoveAsync(BuildCacheKey(_db.CurrentTenantId, templateCode), cancellationToken);

    /// <summary>按编码查询模板实体（未跟踪）。</summary>
    private async Task<PrintTemplate?> FindByCodeInternalAsync(string templateCode, CancellationToken cancellationToken)
        => await _db.PrintTemplates
            .AsNoTracking()
            .Where(t => t.TemplateCode == templateCode)
            .OrderByDescending(static t => t.Version)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>实体 → 契约定义投影（ModuleCode 由 BizType 承载，见实体注释）。</summary>
    private static async Task<PrintTemplateInfo?> QueryTemplateInfoAsync(
        Task<PrintTemplate?> query, CancellationToken cancellationToken)
    {
        var template = await query.ConfigureAwait(false);
        return template is null
            ? null
            : new PrintTemplateInfo(template.TemplateCode, template.Name, template.BizType, template.ContentHtml, template.Css);
    }

    /// <summary>视图投影表达式（查询 / 缓存加载共用，避免投影漂移）。</summary>
    private static System.Linq.Expressions.Expression<System.Func<PrintTemplate, PrintTemplateView>> ToViewExpression()
        => static t => new PrintTemplateView(
            t.Id, t.TemplateCode, t.Name, t.BizType, t.ContentHtml, t.Css,
            t.Status, t.Version, t.CreatedAt, t.UpdatedAt);

    /// <summary>实体 → 视图。</summary>
    private static PrintTemplateView ToView(PrintTemplate template)
        => new(template.Id, template.TemplateCode, template.Name, template.BizType,
            template.ContentHtml, template.Css, template.Status, template.Version,
            template.CreatedAt, template.UpdatedAt);
}
