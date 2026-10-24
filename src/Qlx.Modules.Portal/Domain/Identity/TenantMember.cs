/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 描述：租户成员实体：一个门户用户在某租户内的本地身份（一人多租户，架构规格 6.1）
 */

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Portal.Domain.Identity;

/// <summary>
/// 租户内成员角色。
/// </summary>
public enum TenantMemberRole
{
    /// <summary>租户管理员（本租户内全部权限）。</summary>
    TenantAdmin = 1,

    /// <summary>普通成员（权限由角色体系分配）。</summary>
    OrdinaryMember = 2,

    /// <summary>待接受邀请（尚未激活，禁止登录该租户上下文）。</summary>
    PendingInvite = 3,
}

/// <summary>
/// 租户内成员状态（租户本地开关，与门户级 <see cref="UserStatus"/> 独立）。
/// </summary>
public enum MemberStatus
{
    /// <summary>本租户内启用，允许进入租户上下文。</summary>
    Enabled = 1,

    /// <summary>本租户内停用：该成员在本租户上下文的 Token 失效。</summary>
    Disabled = 2,
}

/// <summary>
/// 租户成员实体（表前缀 por_）。表达"门户用户 ↔ 租户"的多对多成员关系，
/// 承载租户内显示名、成员角色与租户内本地开关（架构规格 6.1 三阶段登录的 Phase 3 校验对象）。
/// </summary>
[Table("por_tenant_members")]
[Index(nameof(TenantId), nameof(PortalUserId), IsUnique = true)]
[Index(nameof(PortalUserId))]
[Index(nameof(Status))]
public sealed class TenantMember : Qlx.Abstractions.Domain.AuditableEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>关联的门户用户 Id。</summary>
    public long PortalUserId { get; internal set; }

    /// <summary>租户内显示名称（同一自然人在不同租户可有不同显示名；为空时回落门户级 DisplayName）。</summary>
    [MaxLength(64)]
    public string? DisplayNameInTenant { get; internal set; }

    /// <summary>租户内主要成员角色。</summary>
    public TenantMemberRole Role { get; internal set; } = TenantMemberRole.OrdinaryMember;

    /// <summary>主属组织 Id（预留：人员模块组织树接入后启用）。</summary>
    public long? OrgId { get; internal set; }

    /// <summary>租户内本地状态。</summary>
    public MemberStatus Status { get; private set; } = MemberStatus.Enabled;

    /// <summary>加入租户时间（邀请接受时写入）。</summary>
    public DateTimeOffset? JoinAt { get; internal set; }

    /// <summary>最近一次进入本租户上下文时间（Phase 3 成功时更新）。</summary>
    public DateTimeOffset? LastLoginAt { get; internal set; }

    /// <summary>成员级 Token 版本（租户内停用 / 角色变更时 +1，仅影响本租户上下文 Token）。</summary>
    public long TokenVersion { get; internal set; }

    /// <summary>所属租户（多对一导航，租户侧不建反向集合）。</summary>
    public Domain.Tenancy.Tenant Tenant { get; internal set; } = default!;

    /// <summary>
    /// EF Core 无参构造。
    /// </summary>
    public TenantMember() { }

    /// <summary>
    /// 创建租户成员（邀请 / 种子入口）。
    /// </summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="portalUserId">门户用户 Id。</param>
    /// <param name="role">租户内角色。</param>
    public TenantMember(long tenantId, long portalUserId, TenantMemberRole role = TenantMemberRole.OrdinaryMember)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(portalUserId);
        TenantId = tenantId;
        PortalUserId = portalUserId;
        Role = role;
        Status = MemberStatus.Enabled;
    }

    /// <summary>记录一次进入租户上下文（Phase 3 成功时调用）。</summary>
    public void RegisterLogin() => LastLoginAt = DateTimeOffset.UtcNow;

    /// <summary>租户内停用（token_version 递增使本租户上下文 Token 刷新即失效）。</summary>
    public void Disable()
    {
        Status = MemberStatus.Disabled;
        TokenVersion++;
    }

    /// <summary>恢复本租户内启用。</summary>
    public void Enable() => Status = MemberStatus.Enabled;
}
