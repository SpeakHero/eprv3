/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 描述：认证体系常量：自定义授权类型 URN、Token Claim 名、token_kind 值
 */

using Qlx.Abstractions.Constants;

namespace Qlx.Modules.Portal.Auth;

/// <summary>
/// 认证体系常量（Portal 模块内部使用；前端需同步感知 GrantType URN 与 Claim 名）。
/// </summary>
public static class QlxAuthConstants
{
    /// <summary>自定义授权类型集合。</summary>
    public static class GrantTypes
    {
        /// <summary>Phase 1 身份认证：login_account + password → 身份级 Token + 租户列表。</summary>
        public const string PortalIdentity = "urn:qlx:grant:portal_identity";

        /// <summary>Phase 3 租户上下文：identity refresh_token + 混淆 tenant_id → 租户级 Token。</summary>
        public const string TenantContext = "urn:qlx:grant:tenant_context";

        /// <summary>租户级 Token 静默续期：凭即将过期（或刚过期）的租户 access token 换新租户 access token，无需重新登录。</summary>
        public const string TenantSilent = "urn:qlx:grant:tenant_silent";
    }

    /// <summary>
    /// Token Claim 名别名：唯一实值定义在 <see cref="QlxClaimConstants"/>（全局常量中心），
    /// 此处仅保留模块内既有引用路径，避免跨模块常量重复声明。
    /// </summary>
    public static class Claims
    {
        /// <summary>Token 用途：identity（身份级）/ tenant（租户上下文级）。</summary>
        public const string TokenKind = QlxClaimConstants.TokenKind;

        /// <summary>租户 Id（租户上下文 Token）。</summary>
        public const string TenantId = QlxClaimConstants.TenantId;

        /// <summary>租户编码（租户上下文 Token）。</summary>
        public const string TenantCode = QlxClaimConstants.TenantCode;

        /// <summary>租户成员记录 Id（租户上下文 Token）。</summary>
        public const string MemberId = QlxClaimConstants.MemberId;

        /// <summary>租户内主要角色（TENANT_ADMIN / ORDINARY_MEMBER）。</summary>
        public const string MemberRole = QlxClaimConstants.MemberRole;

        /// <summary>租户内分配的角色编码列表（角色体系接入前为空数组）。</summary>
        public const string RolesInTenant = QlxClaimConstants.RolesInTenant;

        /// <summary>本次进入所选租户角色 Id（混淆串，可空；仅普通成员写入）。</summary>
        public const string MemberRoleId = QlxClaimConstants.MemberRoleId;

        /// <summary>门户为该租户授权的模块编码列表。</summary>
        public const string Modules = QlxClaimConstants.Modules;

        /// <summary>成员在当前租户上下文的权限编码列表（角色权限体系接入前为空数组）。</summary>
        public const string Permissions = QlxClaimConstants.Permissions;

        /// <summary>合并 Token 版本（PortalUser.TokenVersion + TenantMember.TokenVersion，身份级 Token 仅含用户部分）。</summary>
        public const string TokenVersion = QlxClaimConstants.TokenVersion;

        /// <summary>用户级 Token 版本（刷新校验用，身份级与租户级 Token 均携带）。</summary>
        public const string UserTokenVersion = QlxClaimConstants.UserTokenVersion;

        /// <summary>成员级 Token 版本（仅租户上下文 Token 携带；刷新校验用）。</summary>
        public const string MemberTokenVersion = QlxClaimConstants.MemberTokenVersion;

        /// <summary>门户管理员标记（仅门户管理员 Token 携带）。</summary>
        public const string PortalAdminFlag = QlxClaimConstants.PortalAdminFlag;

        /// <summary>登录账号（身份级与租户级 Token 均携带，供审计 ChangedBy 与前端展示）。</summary>
        public const string LoginAccount = QlxClaimConstants.LoginAccount;
    }

    /// <summary>登录安全缓存键前缀（密码失败计数 / 账号锁定标记）。</summary>
    public static class AuthCacheKeys
    {
        /// <summary>密码失败计数键前缀（auth:pwfail:{loginAccount}）。</summary>
        public const string PasswordFailPrefix = "auth:pwfail:";

        /// <summary>账号锁定标记键前缀（auth:lockout:{loginAccount}）。</summary>
        public const string LockoutPrefix = "auth:lockout:";
    }

    /// <summary>token_kind 取值。</summary>
    public static class TokenKinds
    {
        /// <summary>身份级 Token（无租户绑定，跨租户通用）。</summary>
        public const string Identity = "identity";

        /// <summary>租户上下文 Token（携带完整租户 claims）。</summary>
        public const string Tenant = "tenant";
    }

    /// <summary>授权策略名。</summary>
    public static class Policies
    {
        /// <summary>门户管理员策略：身份级 Token 携带 is_portal_admin=1 方可访问 /api/portal/admin/**。</summary>
        public const string PortalAdmin = "QlxPortalAdmin";
    }
}
