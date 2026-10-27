/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：全局授权策略名常量与权限策略名构造，唯一入口，禁止各模块散落字面量
 */

namespace Qlx.Abstractions.Constants;

/// <summary>
/// 全局授权策略名常量。
/// <para>
/// 权限点策略命名约定：<c>Permission:{permissionCode}</c>，由
/// <c>Qlx.BuildingBlocks.Authorization.PermissionPolicyConfigurer</c> 按模块 Manifest
/// 权限定义自动注册；控制器侧经 <see cref="Permission"/> 构造策略名标注。
/// </para>
/// </summary>
public static class QlxPolicyNames
{
    /// <summary>权限点策略名前缀。</summary>
    public const string PermissionPrefix = "Permission:";

    /// <summary>租户上下文前置策略：仅租户级 Token（携带 tenant_id claim）可访问租户侧 API。</summary>
    public const string TenantContext = "QlxTenantContext";

    /// <summary>构造权限点策略名（<c>Permission:{code}</c>）。</summary>
    /// <param name="permissionCode">权限码（模块 Manifest 声明）。</param>
    /// <returns>授权策略名。</returns>
    public static string Permission(string permissionCode) => $"{PermissionPrefix}{permissionCode}";
}
