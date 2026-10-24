/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 22:10:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 22:10:00 +08:00 中国标准时间 星期四
 * 描述：Token Claim 值常量（member_role 等），全局唯一入口，禁止各模块硬编码字符串
 */

namespace Qlx.Abstractions.Constants;

/// <summary>
/// 租户成员角色值常量。用于 Token claim member_role 的值
/// 与权限拦截器/菜单 API 的零计算旁路判定。
/// </summary>
public static class QlxMemberRoleValues
{
    /// <summary>租户管理员（零计算旁路，仅认此字段，不枚举权限码）。</summary>
    public const string TenantAdmin = "TenantAdmin";

    /// <summary>普通成员（需按角色聚合权限码写入 permissions_claim）。</summary>
    public const string OrdinaryMember = "OrdinaryMember";
}
