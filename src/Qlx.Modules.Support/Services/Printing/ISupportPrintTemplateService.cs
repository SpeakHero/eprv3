/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 13:20:00 +08:00 中国标准时间 星期五
 * 描述：打印模板管理契约（模块级 CRUD，2A 契约 IPrintTemplateService 之外的模块门面能力）
 */

using Qlx.Modules.Support.Domain;

namespace Qlx.Modules.Support.Services.Printing;

/// <summary>打印模板视图（管理端 / 渲染缓存复用）。</summary>
/// <param name="Id">模板 Id（对外经 Hashids 混淆）。</param>
/// <param name="TemplateCode">模板编码。</param>
/// <param name="Name">模板名称。</param>
/// <param name="BizType">业务类型。</param>
/// <param name="ContentHtml">模板 HTML（含占位符）。</param>
/// <param name="Css">附加 CSS（可空）。</param>
/// <param name="Status">模板状态。</param>
/// <param name="Version">版本号。</param>
/// <param name="CreatedAt">创建时间（UTC）。</param>
/// <param name="UpdatedAt">最近更新时间（UTC）。</param>
public sealed record PrintTemplateView(
    long Id,
    string TemplateCode,
    string Name,
    string BizType,
    string ContentHtml,
    string? Css,
    PrintTemplateStatus Status,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>创建打印模板入参。</summary>
/// <param name="TemplateCode">模板编码（同租户唯一）。</param>
/// <param name="Name">模板名称。</param>
/// <param name="BizType">业务类型。</param>
/// <param name="ContentHtml">模板 HTML（含占位符）。</param>
/// <param name="Css">附加 CSS（可空）。</param>
public sealed record PrintTemplateCreateRequest(
    string TemplateCode,
    string Name,
    string BizType,
    string ContentHtml,
    string? Css = null);

/// <summary>更新打印模板入参（内容变更版本自增）。</summary>
/// <param name="Name">模板名称。</param>
/// <param name="BizType">业务类型。</param>
/// <param name="ContentHtml">模板 HTML（含占位符）。</param>
/// <param name="Css">附加 CSS（可空）。</param>
public sealed record PrintTemplateUpdateRequest(
    string Name,
    string BizType,
    string ContentHtml,
    string? Css = null);

/// <summary>
/// 打印模板管理契约（CRUD + 启停）。实现类同时承载 2A 冻结契约
/// <c>IPrintTemplateService</c>（渲染 / 定义查询）。
/// </summary>
public interface ISupportPrintTemplateService
{
    /// <summary>创建打印模板（初始 Draft，版本 1）。</summary>
    /// <param name="request">创建入参。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>模板视图。</returns>
    /// <exception cref="Qlx.Abstractions.Exceptions.ConflictException">模板编码重复。</exception>
    Task<PrintTemplateView> CreateAsync(PrintTemplateCreateRequest request, CancellationToken cancellationToken);

    /// <summary>更新打印模板内容（版本自增，状态不变）。</summary>
    /// <param name="id">模板 Id。</param>
    /// <param name="request">更新入参。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>模板视图。</returns>
    Task<PrintTemplateView> UpdateAsync(long id, PrintTemplateUpdateRequest request, CancellationToken cancellationToken);

    /// <summary>设置模板状态（启用 / 停用 / 草稿回退）。</summary>
    /// <param name="id">模板 Id。</param>
    /// <param name="status">目标状态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task SetStatusAsync(long id, PrintTemplateStatus status, CancellationToken cancellationToken);

    /// <summary>查询全部打印模板（当前租户，按编码 + 版本排序）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>模板视图列表。</returns>
    Task<IReadOnlyList<PrintTemplateView>> ListAsync(CancellationToken cancellationToken);

    /// <summary>按编码查询打印模板。</summary>
    /// <param name="templateCode">模板编码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>模板视图（未找到返回 null）。</returns>
    Task<PrintTemplateView?> GetByCodeAsync(string templateCode, CancellationToken cancellationToken);
}
