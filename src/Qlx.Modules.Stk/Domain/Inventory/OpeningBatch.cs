/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：期初建账单头实体（stk_opening_batches）——单号由后端生成（前端不传码，N-1），
 *       状态机 Draft → Pending → Posted / Pending → Draft，每仓库仅允许一个已过账批次
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;

namespace Qlx.Modules.Stk.Domain.Inventory;

/// <summary>期初建账状态。</summary>
public enum OpeningBatchStatus
{
    /// <summary>草稿（可改删）。</summary>
    Draft = 1,

    /// <summary>待审批（已提交过账申请）。</summary>
    Pending = 2,

    /// <summary>已过账（台账已落账，不可再改）。</summary>
    Posted = 3,
}

/// <summary>
/// 期初建账单头（表 stk_opening_batches）。单号后端生成（N-1）；状态机非法流转统一 409
/// <c>STK_INVALID_STATE</c>；「每仓库唯一已过账」由过滤唯一索引（status = 3）兜底。
/// </summary>
[Index(nameof(TenantId), nameof(BatchNo), IsUnique = true)]
public sealed class OpeningBatch : AuditableEntity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>期初单号（租户内唯一，后端生成后不可修改）。</summary>
    [MaxLength(32)]
    public string BatchNo { get; internal set; } = string.Empty;

    /// <summary>仓库 Id（原始引用，不联表取名称）。</summary>
    public long WarehouseId { get; private set; }

    /// <summary>状态。</summary>
    public OpeningBatchStatus Status { get; private set; } = OpeningBatchStatus.Draft;

    /// <summary>过账时间（仅已过账批次有值）。</summary>
    public DateTimeOffset? PostedAt { get; private set; }

    /// <summary>备注。</summary>
    [MaxLength(256)]
    public string? Remark { get; private set; }

    /// <summary>EF 物化用无参构造。</summary>
    public OpeningBatch() { }

    /// <summary>新建期初草稿。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="batchNo">期初单号（后端生成）。</param>
    /// <param name="warehouseId">仓库 Id。</param>
    /// <param name="remark">备注。</param>
    public OpeningBatch(long tenantId, string batchNo, long warehouseId, string? remark)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(batchNo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        TenantId = tenantId;
        BatchNo = batchNo;
        WarehouseId = warehouseId;
        Remark = remark;
    }

    /// <summary>修改草稿（仅草稿可改）。</summary>
    /// <param name="warehouseId">仓库 Id。</param>
    /// <param name="remark">备注。</param>
    public void UpdateDraft(long warehouseId, string? remark)
    {
        EnsureDraft();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        WarehouseId = warehouseId;
        Remark = remark;
    }

    /// <summary>提交过账审批（Draft → Pending）。</summary>
    public void Submit()
    {
        EnsureDraft();
        Status = OpeningBatchStatus.Pending;
    }

    /// <summary>审批驳回回落草稿（Pending → Draft）。</summary>
    public void Reject()
    {
        EnsurePending();
        Status = OpeningBatchStatus.Draft;
    }

    /// <summary>标记已过账（Pending → Posted）。</summary>
    /// <param name="postedAt">过账时间。</param>
    public void MarkPosted(DateTimeOffset postedAt)
    {
        EnsurePending();
        Status = OpeningBatchStatus.Posted;
        PostedAt = postedAt;
    }

    /// <summary>断言当前为草稿状态（否则 409 <c>STK_INVALID_STATE</c>）。</summary>
    /// <exception cref="ConflictException">非草稿状态时抛出。</exception>
    public void EnsureDraft()
    {
        if (Status != OpeningBatchStatus.Draft)
        {
            throw new ConflictException("仅草稿可操作", "STK_INVALID_STATE");
        }
    }

    private void EnsurePending()
    {
        if (Status != OpeningBatchStatus.Pending)
        {
            throw new ConflictException("仅待审批可操作", "STK_INVALID_STATE");
        }
    }
}
