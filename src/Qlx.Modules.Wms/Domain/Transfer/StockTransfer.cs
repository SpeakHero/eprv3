/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：物资调拨单实体（wms_stock_transfers）——单号由 WL-DB 后端生成，状态机仅经领域方法流转
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Wms.Domain.Transfer;

/// <summary>
/// 物资调拨单（表 wms_stock_transfers）。调出仓与调入仓必须不同；
/// 组织归属只存 Id（跨公司判定依据），不做展示拼装（B-2）。
/// </summary>
[Index(nameof(TenantId), nameof(TransferNo), IsUnique = true)]
[Index(nameof(TenantId), nameof(Status))]
[Index(nameof(TenantId), nameof(FromWarehouseId))]
[Index(nameof(TenantId), nameof(ToWarehouseId))]
public sealed class StockTransfer : AuditableEntity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>调拨单号（租户内唯一，不可修改）。</summary>
    [MaxLength(32)]
    public string TransferNo { get; internal set; } = string.Empty;

    /// <summary>调出仓库 Id。</summary>
    public long FromWarehouseId { get; internal set; }

    /// <summary>调入仓库 Id。</summary>
    public long ToWarehouseId { get; internal set; }

    /// <summary>调出仓归属组织 Id（可为空，空参与跨公司判定）。</summary>
    public long? FromOrgId { get; internal set; }

    /// <summary>调入仓归属组织 Id（可为空，空参与跨公司判定）。</summary>
    public long? ToOrgId { get; internal set; }

    /// <summary>预计在途天数（发货时据此计算预计到达）。</summary>
    public int InTransitDays { get; internal set; }

    /// <summary>备注。</summary>
    [MaxLength(256)]
    public string? Remark { get; internal set; }

    /// <summary>调拨状态。</summary>
    public TransferStatus Status { get; private set; } = TransferStatus.PendingApproval;

    /// <summary>调拨明细行。</summary>
    public List<StockTransferLine> Lines { get; private set; } = [];

    /// <summary>EF 物化用无参构造。</summary>
    public StockTransfer() { }

    /// <summary>创建调拨单（新建即待审批）。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="transferNo">调拨单号（后端生成）。</param>
    /// <param name="fromWarehouseId">调出仓库 Id。</param>
    /// <param name="toWarehouseId">调入仓库 Id。</param>
    /// <param name="fromOrgId">调出仓归属组织 Id。</param>
    /// <param name="toOrgId">调入仓归属组织 Id。</param>
    /// <param name="inTransitDays">预计在途天数。</param>
    /// <param name="remark">备注。</param>
    public StockTransfer(
        long tenantId,
        string transferNo,
        long fromWarehouseId,
        long toWarehouseId,
        long? fromOrgId,
        long? toOrgId,
        int inTransitDays,
        string? remark)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transferNo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fromWarehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(toWarehouseId);
        if (fromWarehouseId == toWarehouseId)
        {
            throw new ArgumentException("调出仓与调入仓不能相同", nameof(toWarehouseId));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(inTransitDays, 1);
        TenantId = tenantId;
        TransferNo = transferNo;
        FromWarehouseId = fromWarehouseId;
        ToWarehouseId = toWarehouseId;
        FromOrgId = fromOrgId;
        ToOrgId = toOrgId;
        InTransitDays = inTransitDays;
        Remark = remark;
    }

    /// <summary>追加明细行（明细从属调拨单，Id 由 EF 在保存时回填）。</summary>
    /// <param name="line">明细行。</param>
    public void AddLine(StockTransferLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        Lines.Add(line);
    }

    /// <summary>审批通过：待审批 → 已生效。</summary>
    public void Activate()
    {
        if (Status != TransferStatus.PendingApproval)
        {
            throw new InvalidOperationException("仅待审批的调拨单可生效");
        }

        Status = TransferStatus.Effective;
    }

    /// <summary>发货：已生效 → 调出已发货。</summary>
    public void Ship()
    {
        if (Status != TransferStatus.Effective)
        {
            throw new InvalidOperationException("仅已生效的调拨单可发货");
        }

        Status = TransferStatus.Shipped;
    }

    /// <summary>收货：调出已发货 → 调入已收货。</summary>
    public void Receive()
    {
        if (Status != TransferStatus.Shipped)
        {
            throw new InvalidOperationException("仅调出已发货的调拨单可收货");
        }

        Status = TransferStatus.Received;
    }

    /// <summary>作废：待审批/已生效 → 已作废（已发货/已收货禁止）。</summary>
    public void Void()
    {
        if (Status is not (TransferStatus.PendingApproval or TransferStatus.Effective))
        {
            throw new InvalidOperationException("仅待审批或已生效的调拨单可作废");
        }

        Status = TransferStatus.Voided;
    }
}
