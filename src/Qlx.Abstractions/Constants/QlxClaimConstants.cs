/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 20:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 20:00:00 +08:00 中国标准时间 星期四
 * 描述：跨模块共享的 Token Claim 名常量，全局唯一入口，禁止各模块硬编码 claim 字符串
 */

namespace Qlx.Abstractions.Constants;

/// <summary>
/// 全局统一的 Token Claim 名常量。
/// <para>
/// 签发方（Portal 认证端点）、消费方（租户上下文中间件、当前用户上下文、黑名单中间件）
/// 必须从此类引用，禁止各自声明私有常量，避免 claim 名漂移导致认证静默失效。
/// </para>
/// </summary>
public static class QlxClaimConstants
{
    /// <summary>JWT 标准主体标识（对外为 Hashids 混淆串）。</summary>
    public const string Subject = "sub";

    /// <summary>JWT 标准显示名称。</summary>
    public const string Name = "name";

    /// <summary>JWT 标准令牌唯一标识（黑名单即时拦截依据）。</summary>
    public const string JwtId = "jti";

    /// <summary>JWT 标准过期时间（Unix 秒）。</summary>
    public const string Expiration = "exp";

    /// <summary>登录账号（全局唯一，跨租户通用）。</summary>
    public const string LoginAccount = "login_account";

    /// <summary>租户 Id（租户上下文 Token，对外为 Hashids 混淆串）。</summary>
    public const string TenantId = "tenant_id";

    /// <summary>租户编码。</summary>
    public const string TenantCode = "tenant_code";

    /// <summary>租户成员记录 Id。</summary>
    public const string MemberId = "member_id";

    /// <summary>租户内主要角色。</summary>
    public const string MemberRole = "member_role";

    /// <summary>租户内分配的角色编码列表。</summary>
    public const string RolesInTenant = "roles_in_tenant";

    /// <summary>本次进入所选租户角色 Id（Hashids 混淆串，可空）。</summary>
    public const string MemberRoleId = "member_role_id";

    /// <summary>租户已授权模块编码列表。</summary>
    public const string Modules = "modules";

    /// <summary>当前租户上下文权限编码列表。</summary>
    public const string Permissions = "permissions";

    /// <summary>Token 用途（identity / tenant）。</summary>
    public const string TokenKind = "token_kind";

    /// <summary>合并 Token 版本。</summary>
    public const string TokenVersion = "token_version";

    /// <summary>用户级 Token 版本（刷新校验用）。</summary>
    public const string UserTokenVersion = "tv_u";

    /// <summary>成员级 Token 版本（仅租户上下文 Token 携带）。</summary>
    public const string MemberTokenVersion = "tv_m";

    /// <summary>门户管理员标记。</summary>
    public const string PortalAdminFlag = "is_portal_admin";

    /// <summary>数据范围（租户上下文 Token，值为 All / DeptAndChildren / Dept / Self）。</summary>
    public const string DataScope = "qlx_data_scope";

    /// <summary>数据范围关联的部门 Id（可选，DeptAndChildren 场景使用）。</summary>
    public const string DataScopeDeptId = "qlx_data_scope_dept";
}
