/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 23:10:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 23:10:00 +08:00 中国标准时间 星期四
 * 描述：门户侧角色权限只读查询契约，Portal 认证服务消费、Portal 模块自身实现
 */

namespace Qlx.Abstractions.Queries;

/// <summary>
/// 门户侧角色权限只读查询契约。
/// Portal 认证服务在身份级 Token 签发阶段需要读取 por_user_roles → por_roles → por_role_permissions
/// 聚合普通门户用户的权限码（PortalAdminFlag 用户走零计算通道，不查权限）。
/// <para>Portal 模块负责实现本接口；消费方禁止自行跨表 Join，一律经本接口。</para>
/// </summary>
public interface IPortalRolePermissionQuery : IModuleQuery
{
    /// <summary>
    /// 查询某门户用户在门户侧的全部有效权限编码（聚合其关联的所有门户角色的 PermissionCode 去重）。
    /// 仅当用户不是 PortalAdmin 时调用（PortalAdmin 走零计算通道）。
    /// </summary>
    /// <param name="portalUserId">门户用户 Id（por_users.id）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>去重后的权限编码列表；无权限则返回空数组。</returns>
    Task<IReadOnlyList<string>> ListUserPermissionCodesAsync(
        long portalUserId, CancellationToken cancellationToken);
}
