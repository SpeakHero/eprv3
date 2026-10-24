/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 14:20:00 +08:00 中国标准时间 星期五
 * 描述：导入导出任务结果载荷 record（调度器 CompletedResult.ResultJson 承载，进程内传递真实 long Id）
 */

using Qlx.Abstractions.ImportExport;

namespace Qlx.Modules.Support.Jobs.ImportExport;

/// <summary>导出任务结果载荷（产出文件已落文件中心临时域）。</summary>
/// <param name="FileId">产出文件 Id（StorageObject.Id）。</param>
/// <param name="FileName">导出文件名。</param>
/// <param name="StorageKey">存储键。</param>
/// <param name="RowCount">导出行数。</param>
/// <param name="DurationMs">总耗时（毫秒）。</param>
public sealed record ExportJobPayload(
    long FileId,
    string FileName,
    string StorageKey,
    long RowCount,
    long DurationMs);

/// <summary>批量导入任务结果载荷。</summary>
/// <param name="TemplateCode">导入模板编码。</param>
/// <param name="TotalRows">总行数。</param>
/// <param name="SuccessRows">成功行数。</param>
/// <param name="FailedRows">失败行数（同行多列错误只计一次）。</param>
/// <param name="Errors">错误明细（行号 + 列名 + 原因）。</param>
/// <param name="DurationMs">总耗时（毫秒）。</param>
public sealed record BatchImportJobPayload(
    string TemplateCode,
    int TotalRows,
    int SuccessRows,
    int FailedRows,
    IReadOnlyList<ImportRowError> Errors,
    long DurationMs);
