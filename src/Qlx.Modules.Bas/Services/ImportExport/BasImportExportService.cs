/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 02:00:00 +08:00 中国标准时间 星期一
 * 描述：Bas 导入导出服务实现——模板清单/下载 + 租户 Guid 映射 + 模板级权限二次校验 + 委托通用导入导出
 */

using System.Security.Claims;

using Microsoft.AspNetCore.Http;

using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.ImportExport;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.ImportExport;

namespace Qlx.Modules.Bas.Services.ImportExport;

/// <summary>
/// Bas 导入导出服务实现。<para>
/// 控制器只声明租户上下文（<c>RequireTenantContext</c>），本服务按模板编码二次校验业务权限，
/// 绕过前端直调 API 同样被拦截（V-2）；租户 Guid 由当前租户上下文单向映射，不接受请求传入。</para>
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="importExport">通用导入导出服务。</param>
/// <param name="definitions">Bas 模板定义提供方（导入 / 导出共 8 份）。</param>
/// <param name="httpContextAccessor">当前请求（权限 claim 读取）。</param>
internal sealed class BasImportExportService(
    ITenantContext tenantCtx,
    IImportExportService importExport,
    BasImportTemplateDefinitionProvider definitions,
    IHttpContextAccessor httpContextAccessor) : IBasImportExportService
{
    private const string ImportSuffix = ".import";

    private const string ExportSuffix = ".export";

    /// <summary>模板编码 → 所需业务权限码（模板级二次校验）。</summary>
    private static readonly Dictionary<string, string> s_permissionByTemplate = new(StringComparer.Ordinal)
    {
        [BasImportTemplateDefinitionProvider.SupplierImport] = "bas.supplier.action",
        [BasImportTemplateDefinitionProvider.SupplierExport] = "bas.supplier.action",
        [BasImportTemplateDefinitionProvider.CustomerImport] = "bas.customer.action",
        [BasImportTemplateDefinitionProvider.CustomerExport] = "bas.customer.action",
        [BasImportTemplateDefinitionProvider.PersonImport] = "bas.person.action",
        [BasImportTemplateDefinitionProvider.PersonExport] = "bas.person.action",
        [BasImportTemplateDefinitionProvider.CategoryImport] = "bas.category.action",
        [BasImportTemplateDefinitionProvider.CategoryExport] = "bas.category.action",
    };

    private long TenantId => tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    /// <inheritdoc />
    public async Task<IReadOnlyList<ImportTemplateInfo>> ListTemplatesAsync(CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        return
        [
            .. definitions.GetDefinitions()
                .Where(static d => d.TemplateCode.EndsWith(ImportSuffix, StringComparison.Ordinal))
                .Select(static d => new ImportTemplateInfo(
                    d.TemplateCode,
                    d.Name,
                    d.ModuleCode,
                    [
                        .. d.Columns.Select(static c => new ImportTemplateColumn(
                            c.ColumnName, c.DisplayName, c.IsRequired, c.DataType, c.Description)),
                    ])),
        ];
    }

    /// <inheritdoc />
    public async Task<ImportTemplateFile> DownloadTemplateAsync(string templateCode, CancellationToken ct)
    {
        ImportTemplateDefinition definition = RequireDefinition(templateCode, ImportSuffix, "导入模板");
        EnsureTemplatePermission(definition.TemplateCode);

        if (importExport is not ImportExportService concrete)
        {
            throw new ConflictException(
                "当前导入导出实现不支持模板下载", "IMPORT_EXPORT_TEMPLATE_UNSUPPORTED");
        }

        byte[] content = await concrete.GenerateTemplateAsync(definition.TemplateCode, ct).ConfigureAwait(false);
        return new ImportTemplateFile(
            content,
            $"{definition.TemplateCode}.xlsx",
            ImportExportService.XlsxContentType);
    }

    /// <inheritdoc />
    public async Task<ImportResult> ImportAsync(string templateCode, Stream stream, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stream);

        ImportTemplateDefinition definition = RequireDefinition(templateCode, ImportSuffix, "导入模板");
        EnsureTemplatePermission(definition.TemplateCode);

        return await importExport
            .ImportAsync(stream, definition.TemplateCode, GuidIdMapper.ToGuid(TenantId), ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ExportResult> ExportAsync(
        string templateCode, IReadOnlyDictionary<string, object?> criteria, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        ImportTemplateDefinition definition = RequireDefinition(templateCode, ExportSuffix, "导出模板");
        EnsureTemplatePermission(definition.TemplateCode);

        return await importExport
            .ExportAsync(definition.TemplateCode, criteria, GuidIdMapper.ToGuid(TenantId), ct)
            .ConfigureAwait(false);
    }

    /// <summary>按操作类型查找模板定义（编码为空 / 后缀不符 / 未注册分别返回 422 / 404）。</summary>
    private ImportTemplateDefinition RequireDefinition(string templateCode, string suffix, string resourceName)
    {
        string normalized = string.IsNullOrWhiteSpace(templateCode)
            ? throw new ValidationException("templateCode", "模板编码不能为空")
            : templateCode.Trim();

        if (!normalized.EndsWith(suffix, StringComparison.Ordinal))
        {
            throw new NotFoundException(resourceName, normalized);
        }

        return definitions.GetDefinitions()
            .FirstOrDefault(d => string.Equals(d.TemplateCode, normalized, StringComparison.Ordinal))
            ?? throw new NotFoundException(resourceName, normalized);
    }

    /// <summary>
    /// 模板级权限二次校验：租户上下文 TenantAdmin / 门户上下文 is_portal_admin=1 旁路，
    /// 否则要求 <c>permissions</c> claim 精确包含模板所属业务权限码。
    /// </summary>
    private void EnsureTemplatePermission(string templateCode)
    {
        if (!s_permissionByTemplate.TryGetValue(templateCode, out string? permission))
        {
            throw new ForbiddenException("无权限执行此操作");
        }

        ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            throw new ForbiddenException("未识别到登录身份");
        }

        if (user.FindFirst(QlxClaimConstants.TenantId) is not null)
        {
            if (string.Equals(
                user.FindFirstValue(QlxClaimConstants.MemberRole),
                QlxMemberRoleValues.TenantAdmin,
                StringComparison.Ordinal))
            {
                return;
            }
        }
        else if (string.Equals(
            user.FindFirstValue(QlxClaimConstants.PortalAdminFlag), "1", StringComparison.Ordinal))
        {
            return;
        }

        if (!ContainsPermission(user, permission))
        {
            throw new ForbiddenException("无权限执行此操作");
        }
    }

    private static bool ContainsPermission(ClaimsPrincipal user, string permission)
    {
        string? claimValue = user.FindFirstValue(QlxClaimConstants.Permissions);
        if (string.IsNullOrWhiteSpace(claimValue))
        {
            return false;
        }

        foreach (string token in claimValue.Split(
            [';', ',', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(token, permission, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
