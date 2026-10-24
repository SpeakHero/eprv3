/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 描述：门户用户实体：全局唯一登录身份（一人一账号走遍所有租户，架构规格 6.3.1）
 */

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Portal.Domain.Identity;

/// <summary>
/// 门户用户状态。
/// </summary>
public enum UserStatus
{
    /// <summary>正常启用，允许登录。</summary>
    Enabled = 1,

    /// <summary>门户级停用：所有租户上下文的 Token 全部失效。</summary>
    Disabled = 2,
}

/// <summary>
/// 门户用户实体（表前缀 por_）。承载全局唯一登录身份与凭据，
/// 密码在门户层统一管理（架构规格 6.3.1：不再支持租户级独立密码）。
/// 与租户的关联关系由 <see cref="TenantMember"/> 一对多表达（一人多租户）。
/// </summary>
[Table("por_users")]
[Index(nameof(LoginAccount), IsUnique = true)]
[Index(nameof(Status))]
public sealed class PortalUser : Qlx.Abstractions.Domain.AuditableEntity
{
    /// <summary>登录账号（手机号 / 证件号 / 自定义账号），全局唯一。</summary>
    [MaxLength(64)]
    public string LoginAccount { get; } = string.Empty;

    /// <summary>密码哈希（PBKDF2，由 Identity PasswordHasher 生成）；外部 IdP 登录用户可为空。</summary>
    [MaxLength(256)]
    public string? PasswordHash { get; internal set; }

    /// <summary>显示名称（允许重名，非唯一标识）。</summary>
    [MaxLength(64)]
    public string DisplayName { get; internal set; } = string.Empty;

    /// <summary>绑定手机号（手机验证码登录的唯一标识）。</summary>
    [MaxLength(20)]
    public string? Phone { get; internal set; }

    /// <summary>绑定邮箱（邮箱验证码登录的唯一标识）。</summary>
    [MaxLength(128)]
    public string? Email { get; internal set; }

    /// <summary>证件号码（实名制关联）。</summary>
    [MaxLength(32)]
    public string? IdCard { get; internal set; }

    /// <summary>是否已完成实名认证。</summary>
    public bool RealNameVerified { get; internal set; }

    /// <summary>头像地址。</summary>
    [MaxLength(512)]
    public string? AvatarUrl { get; internal set; }

    /// <summary>门户级状态。</summary>
    public UserStatus Status { get; private set; } = UserStatus.Enabled;

    /// <summary>是否门户管理员（可查看任意自然人租户关联视图、以审计身份进入租户）。</summary>
    public bool IsPortalAdmin { get; internal set; }

    /// <summary>用户级 Token 版本（密码修改 / 门户停用时 +1，使所有已签发 Token 刷新即失效）。</summary>
    public long TokenVersion { get; internal set; }

    /// <summary>最近一次登录时间（Phase 1 身份认证成功时更新）。</summary>
    public DateTimeOffset? LastLoginAt { get; internal set; }

    /// <summary>租户成员关联集合（一人多租户）。</summary>
    public ICollection<TenantMember> Members { get; internal set; } = [];

    /// <summary>密码历史记录（每次密码修改/重置时新增一条；用于禁止与最近 N 次相同）。</summary>
    public ICollection<PortalUserPasswordHistory> PasswordHistories { get; internal set; } = [];

    /// <summary>
    /// EF Core 无参构造。
    /// </summary>
    public PortalUser() { }

    /// <summary>
    /// 创建门户用户（注册入口，供种子服务 / 管理员操作使用）。
    /// </summary>
    /// <param name="loginAccount">登录账号（全局唯一）。</param>
    /// <param name="displayName">显示名称（允许重名）。</param>
    /// <param name="isPortalAdmin">是否门户管理员。</param>
    public PortalUser(string loginAccount, string displayName, bool isPortalAdmin = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loginAccount);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        LoginAccount = loginAccount;
        DisplayName = displayName;
        IsPortalAdmin = isPortalAdmin;
        Status = UserStatus.Enabled;
    }

    /// <summary>设置密码哈希（由认证服务调用 PasswordHasher 生成后写入）。</summary>
    internal void SetPasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    /// <summary>记录一次成功登录（Phase 1 身份认证成功时调用，拦截器填充 UpdatedAt）。</summary>
    public void RegisterLogin() => LastLoginAt = DateTimeOffset.UtcNow;

    /// <summary>门户级停用（所有租户上下文 Token 经 token_version 递增失效）。</summary>
    public void Disable()
    {
        Status = UserStatus.Disabled;
        TokenVersion++;
    }

    /// <summary>恢复启用。</summary>
    public void Enable() => Status = UserStatus.Enabled;

    /// <summary>密码修改后递增 Token 版本，强制所有设备重新登录（架构规格 6.6.3）。</summary>
    public void BumpTokenVersion() => TokenVersion++;
}
