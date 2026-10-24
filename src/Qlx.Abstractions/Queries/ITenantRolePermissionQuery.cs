/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 22:20:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 22:20:00 +08:00 中国标准时间 星期四
 * 描述：租户角色权限只读查询契约，Portal 认证服务消费、Bas 模块实现，禁止跨库 Join
 */

namespace Qlx.Abstractions.Queries;

/// <summary>
/// 租户角色权限只读查询契约。
/// Portal 认证服务在 Token 签发阶段需要读取 Bas 租户库的
/// 角色-权限分配数据；Bas 模块负责实现本接口。
/// <para>消费方禁止自行跨库 Join；Bas 实现必须强制 TenantId 过滤。</para>
/// </summary>
public interface ITenantRolePermissionQuery : IModuleQuery
{
    /// <summary>
    /// 查询某租户成员的全部有效权限编码（聚合其关联的所有角色的 PermissionCode 去重）。
    /// 仅当成员角色 ≠ TenantAdmin 时调用（TenantAdmin 走零计算通道）。
    /// </summary>
    /// <param name="tenantMemberId">租户成员记录 Id。</param>
    /// <param name="tenantId">租户 Id（必须过滤，禁止跨租户查询）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>去重后的权限编码列表；无权限则返回空数组。</returns>
    Task<IReadOnlyList<string>> ListMemberPermissionCodesAsync(
        long tenantMemberId, long tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// 查询某租户某角色的全部权限编码（用于角色变更验证 / 缓存兜底）。
    /// </summary>
    /// <param name="roleId">角色 Id。</param>
    /// <param name="tenantId">租户 Id（必须过滤）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>该角色关联的所有权限编码；空角色返回空数组。</returns>
    Task<IReadOnlyList<string>> ListRolePermissionCodesAsync(
        long roleId, long tenantId, CancellationToken cancellationToken);
}
