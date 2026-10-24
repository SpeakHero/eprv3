/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：租户注册实体，门户级租户元数据与生命周期控制（含数据库模式）
 */

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Tenancy;

namespace Qlx.Modules.Portal.Domain.Tenancy;

/// <summary>
/// 租户生命周期状态。
/// </summary>
public enum TenantStatus
{
    /// <summary>已注册但数据库尚未初始化。</summary>
    Provisioning = 0,

    /// <summary>正常运行中。</summary>
    Active = 1,

    /// <summary>宽限期内（到期未续费，仍可访问但限制操作）。</summary>
    GracePeriod = 2,

    /// <summary>已注销，数据归档备份中。</summary>
    Archived = 3,

    /// <summary>已物理删除（保留归档备份）。</summary>
    Deleted = 4,

    /// <summary>预配失败（迁移过程中出错，待人工介入）。</summary>
    ProvisioningFailed = 5,

    /// <summary>门户停用：租户下所有用户禁止进入租户上下文，可由门户重新启用。</summary>
    Disabled = 6,
}

/// <summary>
/// 租户注册实体（表前缀 por_）。承载租户的基础元数据、生命周期控制与订阅套餐，
/// 租户数据库连接信息与模块许可通过导航属性关联。
/// 继承 <see cref="Qlx.Abstractions.Domain.AuditableEntity"/>：雪花 Id + CreatedAt/UpdatedAt 由拦截器自动填充。
/// </summary>
[Table("por_tenants")]
[Index(nameof(TenantCode), IsUnique = true)]
[Index(nameof(Status))]
public sealed class Tenant : Qlx.Abstractions.Domain.AuditableEntity
{
    /// <summary>租户唯一编码（全局唯一，门户侧注册时发放，如 t001 / chengdu-qilixin）。</summary>
    [MaxLength(64)]
    public string TenantCode { get; } = string.Empty;

    /// <summary>租户显示名称（集团全称，如四川淇利馨集团有限公司）。</summary>
    [MaxLength(256)]
    public string DisplayName { get; internal set; } = string.Empty;

    /// <summary>简称（如淇利馨集团）。</summary>
    [MaxLength(128)]
    public string? ShortName { get; internal set; }

    /// <summary>统一社会信用代码 / 税号（登录页支持按企业名称或税号直达企业）。</summary>
    [MaxLength(64)]
    public string? TaxNo { get; internal set; }

    /// <summary>行业分类（字典值）。</summary>
    [MaxLength(64)]
    public string? Industry { get; internal set; }

    /// <summary>联系人姓名。</summary>
    [MaxLength(64)]
    public string? ContactName { get; internal set; }

    /// <summary>联系人手机号。</summary>
    [MaxLength(32)]
    public string? ContactPhone { get; internal set; }

    /// <summary>联系人邮箱。</summary>
    [MaxLength(256)]
    public string? ContactEmail { get; internal set; }

    /// <summary>租户订阅套餐标识（套餐定义由门户维护）。</summary>
    [MaxLength(64)]
    public string? PlanCode { get; internal set; }

    /// <summary>订阅开始时间。</summary>
    public DateTimeOffset? SubscribedAt { get; internal set; }

    /// <summary>订阅到期时间。</summary>
    public DateTimeOffset? SubscriptionExpiresAt { get; internal set; }

    /// <summary>宽限期到期时间（到期后物理删除）。</summary>
    public DateTimeOffset? GracePeriodUntil { get; internal set; }

    /// <summary>租户生命周期状态。</summary>
    public TenantStatus Status { get; private set; } = TenantStatus.Provisioning;

    /// <summary>租户数据库模式（默认共享；仅允许 Shared → Isolated 单向升级）。</summary>
    public TenantDatabaseMode DatabaseMode { get; private set; } = TenantDatabaseMode.Shared;

    /// <summary>最近一次预配失败的错误信息（供人工排查）。</summary>
    [MaxLength(4096)]
    public string? LastErrorMessage { get; private set; }

    /// <summary>累计预配尝试次数（含成功与失败）。</summary>
    public int ProvisioningAttemptCount { get; private set; }

    /// <summary>最近一次预配尝试时间（成功或失败都会更新）。</summary>
    public DateTimeOffset? LastProvisioningAttemptAt { get; private set; }

    /// <summary>租户数据库连接信息（一对一导航）。</summary>
    public TenantDatabase? Database { get; internal set; }

    /// <summary>租户模块许可集合（一对多导航）。</summary>
    public ICollection<TenantModule> Modules { get; internal set; } = [];

    /// <summary>
    /// EF Core 无参构造。
    /// </summary>
    public Tenant() { }

    /// <summary>
    /// 创建新租户。门户侧注册入口（供 PortalSeedService / Admin API 使用）。
    /// </summary>
    /// <param name="tenantCode">租户编码（全局唯一）。</param>
    /// <param name="displayName">显示名称。</param>
    public Tenant(string tenantCode, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        TenantCode = tenantCode;
        DisplayName = displayName;
        Status = TenantStatus.Provisioning;
    }

    /// <summary>设置数据库连接信息（一对一导航）。</summary>
    internal void SetDatabase(TenantDatabase database) => Database = database;

    /// <summary>
    /// 变更数据库模式。仅允许 <see cref="TenantDatabaseMode.Shared"/> → <see cref="TenantDatabaseMode.Isolated"/>；
    /// 同值重复变更与反向降级均视为业务冲突。
    /// </summary>
    /// <param name="mode">目标模式。</param>
    /// <exception cref="ConflictException">目标模式非法或与当前模式相同时抛出。</exception>
    public void ChangeDatabaseMode(TenantDatabaseMode mode)
    {
        if (DatabaseMode == mode)
        {
            throw new ConflictException(
                $"租户 {TenantCode} 数据库模式已是 {mode}，无需变更", PortalConstants.ErrorCodes.TenantDatabaseModeUnchanged);
        }

        if (DatabaseMode != TenantDatabaseMode.Shared || mode != TenantDatabaseMode.Isolated)
        {
            throw new ConflictException(
                $"租户 {TenantCode} 数据库模式不允许 {DatabaseMode} → {mode}（仅允许 Shared → Isolated）",
                PortalConstants.ErrorCodes.TenantDatabaseModeInvalid);
        }

        DatabaseMode = mode;
    }

    /// <summary>
    /// 门户注册时写入税务信息与订阅套餐（仅预配/激活阶段可写，后续由续订流程维护）。
    /// </summary>
    /// <param name="taxNo">统一社会信用代码。</param>
    /// <param name="planCode">套餐标识（与前端会员等级对应，如 standard / professional）。</param>
    /// <param name="subscriptionExpiresAt">订阅到期时间（无期限为 null）。</param>
    public void SetRegistrationProfile(string taxNo, string planCode, DateTimeOffset? subscriptionExpiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taxNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(planCode);

        if (Status is not (TenantStatus.Provisioning or TenantStatus.Active))
        {
            throw new InvalidOperationException($"租户 {TenantCode} 当前状态 {Status} 不允许修改注册信息");
        }

        TaxNo = taxNo;
        PlanCode = planCode;
        SubscribedAt ??= DateTimeOffset.UtcNow;
        SubscriptionExpiresAt = subscriptionExpiresAt;
    }

    /// <summary>添加模块许可。</summary>
    internal void AddModule(TenantModule module) => Modules.Add(module);

    /// <summary>
    /// 激活租户（数据库初始化完成后由门户调用）。
    /// </summary>
    public void Activate()
    {
        if (Status is not TenantStatus.Provisioning)
        {
            throw new InvalidOperationException($"租户 {TenantCode} 当前状态 {Status} 不允许激活");
        }

        Status = TenantStatus.Active;
        LastProvisioningAttemptAt = DateTimeOffset.UtcNow;
        LastErrorMessage = null;
    }

    /// <summary>
    /// 标记进入宽限期。
    /// </summary>
    /// <param name="graceDays">宽限期天数。</param>
    public void EnterGracePeriod(int graceDays)
    {
        if (Status is not TenantStatus.Active)
        {
            throw new InvalidOperationException($"租户 {TenantCode} 当前状态 {Status} 不允许进入宽限期");
        }

        Status = TenantStatus.GracePeriod;
        GracePeriodUntil = DateTimeOffset.UtcNow.AddDays(graceDays);
    }

    /// <summary>
    /// 标记预配失败（迁移过程中出错，待人工介入）。
    /// 记录错误信息并递增尝试次数，更新最近尝试时间。
    /// </summary>
    /// <param name="errorMessage">错误描述（建议包含具体模块与异常摘要）。</param>
    public void MarkProvisioningFailed(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        ProvisioningAttemptCount++;
        LastProvisioningAttemptAt = DateTimeOffset.UtcNow;
        LastErrorMessage = errorMessage;
        Status = TenantStatus.ProvisioningFailed;
    }

    /// <summary>
    /// 将失败的预配重置为 Provisioning（人工修复后重新扫描）。
    /// </summary>
    public void MarkProvisioningRetry()
    {
        if (Status is not TenantStatus.ProvisioningFailed)
        {
            throw new InvalidOperationException($"租户 {TenantCode} 当前状态 {Status} 不允许重试预配");
        }

        Status = TenantStatus.Provisioning;
        LastErrorMessage = null;
        LastProvisioningAttemptAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 续订并恢复 Active 状态。
    /// </summary>
    /// <param name="daysToAdd">续订天数。</param>
    public void Renew(int daysToAdd)
    {
        if (Status is not TenantStatus.Active && Status is not TenantStatus.GracePeriod)
        {
            throw new InvalidOperationException($"租户 {TenantCode} 当前状态 {Status} 不允许续订");
        }

        var baseDate = SubscriptionExpiresAt ?? DateTimeOffset.UtcNow;
        SubscriptionExpiresAt = baseDate.AddDays(daysToAdd);
        Status = TenantStatus.Active;
        GracePeriodUntil = null;
    }

    /// <summary>
    /// 门户停用租户（停用后 Phase 3 无法进入该租户上下文，可经 <see cref="Enable"/> 恢复）。
    /// </summary>
    public void Disable()
    {
        if (Status is TenantStatus.Archived or TenantStatus.Deleted)
        {
            throw new InvalidOperationException($"租户 {TenantCode} 已归档或删除，不允许停用");
        }

        Status = TenantStatus.Disabled;
    }

    /// <summary>
    /// 重新启用已停用的租户，恢复为 Active。
    /// </summary>
    public void Enable()
    {
        if (Status is not TenantStatus.Disabled)
        {
            throw new InvalidOperationException($"租户 {TenantCode} 当前状态 {Status} 不允许启用");
        }

        Status = TenantStatus.Active;
    }

    /// <summary>
    /// 注销租户（归档备份中）。
    /// </summary>
    public void Archive()
    {
        if (Status is TenantStatus.Archived or TenantStatus.Deleted)
        {
            return;
        }

        Status = TenantStatus.Archived;
    }
}
