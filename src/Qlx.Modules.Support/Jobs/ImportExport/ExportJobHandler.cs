/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 14:20:00 +08:00 中国标准时间 星期五
 * 描述：大数据量导出任务处理器（ASYNC）：导出投影 → SHA-256 → 落盘文件中心（临时域）→ 落库 → 站内通知
 */

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Qlx.Abstractions.ImportExport;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Messages;
using Qlx.BuildingBlocks.Files;
using Qlx.BuildingBlocks.ImportExport;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Jobs.Files;
using Qlx.Modules.Support.Services.Files;

namespace Qlx.Modules.Support.Jobs.ImportExport;

/// <summary>
/// 大数据量导出处理器（<see cref="ImportExportTaskCodes.Export"/>，ASYNC）。
/// 产出文件落文件中心临时域（/temp/export/yyyy/MM/，TTL 到期由 file.temp.purge 清理），
/// 完成后经 <see cref="IMessageService"/> 站内通知操作人。
/// </summary>
public sealed partial class ExportJobHandler(
    SupportTenantDbContext db,
    IImportExportService importExportService,
    IFileStorageBackend storageBackend,
    SupportSeedService seedService,
    IMessageService messageService,
    ILogger<ExportJobHandler> logger) : IJobHandler
{
    /// <inheritdoc />
    public string TaskCode => ImportExportTaskCodes.Export;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var templateCode = FileJobHelpers.GetRequiredString(context, ImportExportJobParams.TemplateCode);
        var operatorId = FileJobHelpers.GetRequiredString(context, ImportExportJobParams.OperatorId);
        var criteria = context.Parameters.GetValueOrDefault(ImportExportJobParams.Criteria)
            as IReadOnlyDictionary<string, object?> ?? new Dictionary<string, object?>();

        var stopwatch = Stopwatch.StartNew();
        var tenantId = db.CurrentTenantId;

        // 1. 导出投影（BuildingBlocks 引擎：注册表 → 数据源批量拉取 → ClosedXML 工作簿）
        var result = await importExportService
            .ExportAsync(templateCode, criteria, context.TenantId, cancellationToken)
            .ConfigureAwait(false);

        // 2. 产出文件落文件中心（临时域；hash 秒传语义与本域一致，导出重跑同内容可复用物理文件）
        await seedService.EnsureSeededAsync(tenantId, operatorId, cancellationToken).ConfigureAwait(false);
        using var source = result.FileStream;
        var content = source;
        if (!content.CanSeek)
        {
            var buffered = new MemoryStream();
            await content.CopyToAsync(buffered, cancellationToken).ConfigureAwait(false);
            content = buffered;
        }

        content.Position = 0;
        var hash = await FileJobHelpers.ComputeSha256HexAsync(content, cancellationToken).ConfigureAwait(false);
        var sizeBytes = content.Length;
        content.Position = 0;

        var now = DateTimeOffset.UtcNow;
        var storageKey = $"{hash[..2]}/{hash[2..4]}/{hash}.bin";
        await storageBackend.SaveAsync(storageKey, content, setReadOnly: false, cancellationToken).ConfigureAwait(false);

        var path = $"/{SupportFileDomains.Temp}/export/{now:yyyy}/{now:MM}/{result.FileName}";
        var storageObject = new StorageObject(tenantId, null, result.FileName, SupportFileDomains.Temp, path,
            hash, sizeBytes, result.ContentType, operatorId);
        db.StorageObjects.Add(storageObject);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        db.FileVersions.Add(new FileVersion(tenantId, storageObject.Id, 1, hash, sizeBytes, storageKey, operatorId, now));
        db.FileOpLogs.Add(new FileOpLog(tenantId, storageObject.Id, "EXPORT", operatorId, null,
            $"导出任务 {templateCode} 产出 {result.RowCount} 行", now));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        // 3. 完成通知（站内信；通知失败不影响导出成果与任务成功态）
        await messageService.PushAsync(new PushRequest(
            operatorId,
            "导出任务完成",
            $"模板 {templateCode} 导出完成：共 {result.RowCount} 行，文件「{result.FileName}」已存入文件中心临时区（到期自动清理）。",
            BizRef: new MessageBizRef("file.export", storageObject.Id
                .ToString(System.Globalization.CultureInfo.InvariantCulture))), cancellationToken).ConfigureAwait(false);

        LogExportCompleted(templateCode, result.RowCount, storageObject.Id);
        return new CompletedResult(
            new ExportJobPayload(storageObject.Id, result.FileName, storageKey, result.RowCount, stopwatch.ElapsedMilliseconds),
            null, null, stopwatch.ElapsedMilliseconds, IsSuccess: true);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "导出任务完成：模板 {TemplateCode}，行数 {RowCount}，文件 Id {FileId}")]
    private partial void LogExportCompleted(string templateCode, long rowCount, long fileId);
}
