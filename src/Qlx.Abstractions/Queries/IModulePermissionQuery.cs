/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21
 * 描述：跨模块模块定义/权限点/租户授权状态只读查询契约。
 *       Portal 模块实现并注册；业务模块（如 Bas）消费以构建权限点列表和已授权模块卡片。
 */

namespace Qlx.Abstractions.Queries;

/// <summary>
/// 权限点定义（来自 Portal 库 por_permission_definitions）。
/// </summary>
/// <param name="Code">权限码（模块唯一）。</param>
/// <param name="Name">权限点名称。</param>
/// <param name="Description">权限点说明（可为空）。</param>
public record PermissionDefinitionRow(string Code, string Name, string? Description);

/// <summary>
/// 模块定义预览（来自 Portal 库 por_module_definitions）。
/// </summary>
/// <param name="Code">模块编码。</param>
/// <param name="Name">模块名称。</param>
/// <param name="Description">模块说明（可为空）。</param>
/// <param name="Icon">模块图标（可为空）。</param>
/// <param name="Version">版本号（可为空）。</param>
/// <param name="Permissions">本模块下全部权限点。</param>
public record ModuleDefinitionRow(
    string Code,
    string Name,
    string? Description,
    string? Icon,
    string? Version,
    IReadOnlyList<PermissionDefinitionRow> Permissions);

/// <summary>
/// 租户已授权模块卡片预览（来自 por_tenant_modules + por_module_definitions 投影）。
/// </summary>
/// <param name="Code">模块编码。</param>
/// <param name="Name">模块名称。</param>
/// <param name="Description">模块说明。</param>
/// <param name="Icon">模块图标。</param>
/// <param name="Version">版本号。</param>
/// <param name="Enabled">是否启用。</param>
/// <param name="LicensedAt">授权时间（可为空表示未授权）。</param>
public record TenantModuleCardRow(
    string Code,
    string Name,
    string Description,
    string? Icon,
    string? Version,
    bool Enabled,
    DateTimeOffset? LicensedAt);

/// <summary>
/// 跨模块模块定义/权限点/租户授权状态只读查询接口。
/// </summary>
public interface IModulePermissionQuery : IModuleQuery
{
    /// <summary>
    /// 按模块分组返回全部权限定义（前端"功能权限树"下拉数据）。
    /// </summary>
    Task<IReadOnlyList<ModuleDefinitionRow>> ListAllModulesWithPermissionsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 查询指定租户的已授权模块列表（前端"已开通模块"卡片）。
    /// </summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<TenantModuleCardRow>> ListTenantModulesAsync(long tenantId, CancellationToken cancellationToken);
}
