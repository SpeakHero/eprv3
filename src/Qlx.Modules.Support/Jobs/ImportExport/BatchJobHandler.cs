/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 14:20:00 +08:00 中国标准时间 星期五
 * 描述：批量导入任务处理器（ASYNC）：按已上传文件中心 Excel 执行导入 → 结果站内通知（含错误明细摘要）
 */

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.ImportExport;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Messages;
using Qlx.BuildingBlocks.Files;
using Qlx.BuildingBlocks.ImportExport;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Jobs.Files;

namespace Qlx.Modules.Support.Jobs.ImportExport;

/// <summary>
/// 批量导入处理器（<see cref="ImportExportTaskCodes.BatchImport"/>，ASYNC）。
/// 源文件为用户先经文件中心上传的 Excel（fileId 定位最新版本），导入执行
/// （表头校验 → 行级校验 → 字典校验 → 落点批量写入）后经站内信回执结果与错误明细摘要。
/// </summary>
public sealed partial class BatchJobHandler(
    SupportTenantDbContext db,
    IImportExportService importExportService,
    IFileStorageBackend storageBackend,
    IMessageService messageService,
    ILogger<BatchJobHandler> logger) : IJobHandler
{
    /// <summary>结果通知中错误明细摘要的最大条数（完整明细以任务结果载荷为准）。</summary>
    private const int NotifyErrorDetailLimit = 10;

    /// <inheritdoc />
    public string TaskCode => ImportExportTaskCodes.BatchImport;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var templateCode = FileJobHelpers.GetRequiredString(context, ImportExportJobParams.TemplateCode);
        var operatorId = FileJobHelpers.GetRequiredString(context, ImportExportJobParams.OperatorId);
        var fileId = FileJobHelpers.GetOptionalLong(context, ImportExportJobParams.FileId);
        if (fileId <= 0)
        {
            throw new ValidationException(
                ImportExportJobParams.FileId, $"任务 {context.TaskCode} 缺少必填参数 {ImportExportJobParams.FileId}");
        }

        var stopwatch = Stopwatch.StartNew();

        // 1. 定位已上传的导入文件（StorageObject + 最新版本存储键）
        var resourceId = fileId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var storageObject = await db.StorageObjects
            .AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == fileId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("文件", resourceId);
        var version = await db.FileVersions
            .AsNoTracking()
            .Where(v => v.ObjectId == fileId)
            .OrderByDescending(static v => v.VerNo)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("文件版本", resourceId);

        // 2. 读回 Excel 并执行导入（引擎内完成表头 / 行级 / 字典三级校验与落点批量写入）
        ImportResult result;
        await using (var stream = await storageBackend
            .OpenReadAsync(version.StorageKey, cancellationToken).ConfigureAwait(false))
        {
            result = await importExportService
                .ImportAsync(stream, templateCode, context.TenantId, cancellationToken)
                .ConfigureAwait(false);
        }

        stopwatch.Stop();

        // 3. 结果回执（站内信；含行级统计与错误明细摘要，完整明细以任务结果载荷为准）
        await messageService.PushAsync(new PushRequest(
            operatorId,
            "批量导入完成",
            BuildNotifyContent(templateCode, result),
            BizRef: new MessageBizRef("file.import.batch", resourceId)), cancellationToken).ConfigureAwait(false);

        LogBatchImportCompleted(templateCode, result.TotalRows, result.SuccessRows, result.FailedRows);
        return new CompletedResult(
            new BatchImportJobPayload(
                templateCode, result.TotalRows, result.SuccessRows, result.FailedRows,
                result.Errors, stopwatch.ElapsedMilliseconds),
            null, null, stopwatch.ElapsedMilliseconds, IsSuccess: true);
    }

    /// <summary>组装结果通知正文（行级统计 + 前 N 条错误摘要）。</summary>
    private static string BuildNotifyContent(string templateCode, ImportResult result)
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"模板 {templateCode} 导入完成：总行数 {result.TotalRows}，成功 {result.SuccessRows}，失败 {result.FailedRows}。");
        if (result.Errors.Count > 0)
        {
            builder.Append(CultureInfo.InvariantCulture, $"错误明细（前 {NotifyErrorDetailLimit} 条）：");
            foreach (var error in result.Errors.Take(NotifyErrorDetailLimit))
            {
                builder.Append(CultureInfo.InvariantCulture, $"第 {error.RowNumber} 行「{error.ColumnName}」{error.ErrorMessage}；");
            }

            if (result.Errors.Count > NotifyErrorDetailLimit)
            {
                builder.Append(CultureInfo.InvariantCulture, $"……其余 {result.Errors.Count - NotifyErrorDetailLimit} 条见任务执行结果。");
            }
        }

        return builder.ToString();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "批量导入完成：模板 {TemplateCode}，总行数 {TotalRows}，成功 {SuccessRows}，失败 {FailedRows}")]
    private partial void LogBatchImportCompleted(string templateCode, int totalRows, int successRows, int failedRows);
}
