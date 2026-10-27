/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 18:00:00 +08:00 中国标准时间 星期日
 * 描述：客户回款银行账户实体（bas_customer_banks）
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain.Customer;

/// <summary>
/// 客户回款银行账户（表 bas_customer_banks）。仅存主表外键标量 <see cref="CustomerId"/>，不配置 EF 导航属性；
/// 账号属于敏感信息，审计快照按后 4 位脱敏落库（C-7-3）。
/// </summary>
[Index(nameof(TenantId), nameof(CustomerId))]
public sealed class CustomerBank : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>所属客户 Id（bas_customers.id）。</summary>
    public long CustomerId { get; internal set; }

    /// <summary>开户银行。</summary>
    [MaxLength(128)]
    public string BankName { get; internal set; } = string.Empty;

    /// <summary>账户名称。</summary>
    [MaxLength(128)]
    public string AccountName { get; internal set; } = string.Empty;

    /// <summary>银行账号。</summary>
    [MaxLength(64)]
    public string AccountNo { get; internal set; } = string.Empty;

    /// <summary>是否默认账户。</summary>
    public bool IsDefault { get; private set; }

    /// <summary>启停状态。</summary>
    public EntityStatus Status { get; private set; } = EntityStatus.Enabled;

    public CustomerBank() { }

    public CustomerBank(long tenantId, long customerId, string bankName, string accountName, string accountNo, bool isDefault)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bankName);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountNo);
        TenantId = tenantId;
        CustomerId = customerId;
        BankName = bankName;
        AccountName = accountName;
        AccountNo = accountNo;
        IsDefault = isDefault;
    }

    /// <summary>设为默认账户。</summary>
    public void MakeDefault() => IsDefault = true;

    /// <summary>取消默认账户。</summary>
    public void UnsetDefault() => IsDefault = false;

    /// <summary>停用账户。</summary>
    public void Disable() => Status = EntityStatus.Disabled;
}
