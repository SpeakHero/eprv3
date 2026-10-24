/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:50:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:50:00 +08:00 中国标准时间 星期三
 * 描述：导入导出服务契约
 */

namespace Qlx.Abstractions.ImportExport;

/// <summary>
/// 导入导出服务接口。
/// </summary>
public interface IImportExportService
{
    /// <summary>
    /// 从文件导入数据。
    /// </summary>
    /// <param name="stream">输入文件流。</param>
    /// <param name="templateCode">导入模板编码。</param>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>导入结果。</returns>
    Task<ImportResult> ImportAsync(Stream stream, string templateCode, Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// 导出数据为文件。
    /// </summary>
    /// <param name="templateCode">导出模板编码。</param>
    /// <param name="criteria">导出条件（业务模块自定义）。</param>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>导出结果（含文件流）。</returns>
    Task<ExportResult> ExportAsync(string templateCode, IReadOnlyDictionary<string, object?> criteria, Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// 获取导入模板定义。
    /// </summary>
    /// <param name="templateCode">模板编码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>模板定义（未找到返回 null）。</returns>
    Task<ImportTemplateInfo?> GetTemplateAsync(string templateCode, CancellationToken cancellationToken);
}

/// <summary>
/// 导入结果。
/// </summary>
public sealed record ImportResult(
    int TotalRows,
    int SuccessRows,
    int FailedRows,
    IReadOnlyList<ImportRowError> Errors,
    TimeSpan Duration);

/// <summary>
/// 行级导入错误。
/// </summary>
public sealed record ImportRowError(
    int RowNumber,
    string ColumnName,
    string ErrorMessage);

/// <summary>
/// 导出结果。
/// </summary>
public sealed record ExportResult(
    Stream FileStream,
    string FileName,
    string ContentType,
    long RowCount,
    TimeSpan Duration);

/// <summary>
/// 导入模板定义。
/// </summary>
public sealed record ImportTemplateInfo(
    string TemplateCode,
    string Name,
    string ModuleCode,
    IReadOnlyList<ImportTemplateColumn> Columns);

/// <summary>
/// 模板列定义。
/// </summary>
public sealed record ImportTemplateColumn(
    string ColumnName,
    string DisplayName,
    bool IsRequired,
    string? DataType,
    string? Description = null);
