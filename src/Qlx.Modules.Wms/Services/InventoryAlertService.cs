/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存预警服务实现——列表筛选 + 处理（说明必填）/忽略回填，状态机仅供待处理流转
 */

using System.Globalization;

using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Modules.Wms.Domain.Alert;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Repositories;

namespace Qlx.Modules.Wms.Services;

/// <summary>
/// 库存预警服务。预警只由扫描任务生成（wms.alert.scan），接口仅做查询与处理回填；
/// 处理/忽略均要求当前状态为「待处理」，非法流转 409 <c>WMS_INVALID_STATE</c>。
/// </summary>
/// <param name="repo">Wms 租户仓储。</param>
/// <param name="userContext">当前用户上下文（处理人）。</param>
internal sealed class InventoryAlertService(
    IWmsTenantRepository repo,
    ICurrentUserContext userContext) : IInventoryAlertService
{
    /// <summary>状态机非法流转错误码。</summary>
    internal const string ErrorInvalidState = "WMS_INVALID_STATE";

    private const int MaxHandledNoteLength = 256;

    private string? CurrentUserId => userContext.UserId;

    /// <inheritdoc />
    public async Task<IReadOnlyList<InventoryAlertRow>> ListAsync(
        int? alertType, int? status, long? warehouseId, CancellationToken ct)
    {
        if (alertType is < 1 or > 4)
        {
            throw new ValidationException("alertType", "预警类型必须为 1..4");
        }

        if (status is < 1 or > 3)
        {
            throw new ValidationException("status", "状态必须为 1..3");
        }

        if (warehouseId is <= 0)
        {
            throw new ValidationException("warehouseId", "仓库 Id 非法");
        }

        IReadOnlyList<InventoryAlert> alerts = await repo
            .ListInventoryAlertsAsync(alertType, status, warehouseId, ct).ConfigureAwait(false);
        return [.. alerts.Select(Map)];
    }

    /// <inheritdoc />
    public Task<InventoryAlertRow> HandleAsync(
        long id, HandleInventoryAlertPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        string note = RequireHandledNote(payload.HandledNote);
        return UpdateStatusAsync(id, alert =>
        {
            alert.Handle(note, CurrentUserId, DateTimeOffset.UtcNow);
            return Map(alert);
        }, ct);
    }

    /// <inheritdoc />
    public Task<InventoryAlertRow> IgnoreAsync(long id, CancellationToken ct)
        => UpdateStatusAsync(id, alert =>
        {
            alert.Ignore(CurrentUserId, DateTimeOffset.UtcNow);
            return Map(alert);
        }, ct);

    private Task<InventoryAlertRow> UpdateStatusAsync(
        long id, Func<InventoryAlert, InventoryAlertRow> apply, CancellationToken ct)
    {
        if (id <= 0)
        {
            throw new ValidationException("id", "预警 Id 非法");
        }

        return repo.ExecuteInTransactionAsync(async token =>
        {
            InventoryAlert alert = await repo.FindInventoryAlertAsync(id, token).ConfigureAwait(false)
                ?? throw new NotFoundException("库存预警", id.ToString(CultureInfo.InvariantCulture));
            if (alert.Status != InventoryAlertStatus.Pending)
            {
                throw new ConflictException("仅待处理的库存预警可处理或忽略", ErrorInvalidState);
            }

            InventoryAlertRow row = apply(alert);
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return row;
        }, ct);
    }

    private static string RequireHandledNote(string? handledNote)
    {
        if (string.IsNullOrWhiteSpace(handledNote))
        {
            throw new ValidationException("handledNote", "处理说明不能为空");
        }

        string trimmed = handledNote.Trim();
        return trimmed.Length <= MaxHandledNoteLength
            ? trimmed
            : throw new ValidationException("handledNote", $"长度不能超过 {MaxHandledNoteLength} 个字符");
    }

    private static InventoryAlertRow Map(InventoryAlert alert)
        => new(alert.Id, (int)alert.AlertType, alert.ItemId, alert.WarehouseId, alert.BatchNo,
            alert.Threshold, alert.ActualValue, alert.AlertDate, (int)alert.Status,
            alert.HandledBy, alert.HandledNote, alert.HandledAt, alert.CreatedAt);
}
