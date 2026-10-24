/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 12:00:00 +08:00 中国标准时间 星期五
 * 描述：导入导出服务实现（ClosedXML）——模板生成、表头校验、编码/格式/必填校验、错误行明细、导出投影
 */

using System.Diagnostics;
using ClosedXML.Excel;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.ImportExport;

namespace Qlx.BuildingBlocks.ImportExport;

/// <summary>
/// 导入导出服务（ClosedXML 实现，2A 冻结契约 <see cref="IImportExportService"/>）。
/// <para>模板定义经 <see cref="IImportTemplateRegistry"/> 注册；导入校验通过的行批量写入
/// <see cref="IImportRowSink"/>；导出数据经 <see cref="IExportDataProvider"/> 批量拉取投影。
/// 字典列经 <see cref="IImportDictionaryValidator"/> 分批校验（避免逐格往返）。</para>
/// </summary>
public sealed partial class ImportExportService(
    IImportTemplateRegistry registry,
    System.Collections.Generic.IEnumerable<IImportRowSink> sinks,
    System.Collections.Generic.IEnumerable<IExportDataProvider> dataProviders,
    IImportDictionaryValidator? dictionaryValidator = null) : IImportExportService
{
    /// <summary>模板「数据」工作表名（模板生成与导入读取共用）。</summary>
    public const string DataSheetName = "数据";

    /// <summary>模板「说明」工作表名。</summary>
    public const string GuideSheetName = "说明";

    /// <summary>导入文件 MIME 类型。</summary>
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IImportTemplateRegistry _registry = registry;
    private readonly IEnumerable<IImportRowSink> _sinks = sinks;
    private readonly IEnumerable<IExportDataProvider> _dataProviders = dataProviders;
    private readonly IImportDictionaryValidator? _dictionaryValidator = dictionaryValidator;

    /// <inheritdoc />
    public Task<ImportTemplateInfo?> GetTemplateAsync(string templateCode, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateCode);

        var definition = _registry.Find(templateCode);
        if (definition is null)
        {
            return Task.FromResult<ImportTemplateInfo?>(null);
        }

        var columns = definition.Columns
            .Select(static c => new ImportTemplateColumn(
                c.ColumnName, c.DisplayName, c.IsRequired, c.DataType, c.Description))
            .ToList();

        return Task.FromResult<ImportTemplateInfo?>(new ImportTemplateInfo(
            definition.TemplateCode, definition.Name, definition.ModuleCode, columns));
    }

    /// <inheritdoc />
    public async Task<ImportResult> ImportAsync(Stream stream, string templateCode, Guid tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateCode);
        ArgumentNullException.ThrowIfNull(stream);

        var definition = _registry.Find(templateCode)
            ?? throw new NotFoundException("导入模板", templateCode);
        var sink = _sinks.FirstOrDefault(s => string.Equals(s.TemplateCode, templateCode, StringComparison.Ordinal))
            ?? throw new NotFoundException("模板导入行落点（IImportRowSink）", templateCode);

        var stopwatch = Stopwatch.StartNew();
        using var workbook = await LoadWorkbookAsync(stream, cancellationToken).ConfigureAwait(false);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new ConflictException("导入文件不包含任何工作表。", "IMPORT_FILE_INVALID");

        var errors = new List<ImportRowError>();
        var columnsByIndex = MatchHeaders(worksheet, definition, errors);
        if (errors.Count > 0)
        {
            // 表头错误直接整批拒绝，不进入行级校验
            return new ImportResult(0, 0, 0, errors, stopwatch.Elapsed);
        }

        var staged = ParseDataRows(worksheet, definition, columnsByIndex, errors);
        staged = await ValidateDictionaryValuesAsync(definition, staged, errors, cancellationToken).ConfigureAwait(false);

        var successRows = await sink.WriteRowsAsync(
            [.. staged.Select(static r => r.Values)], tenantId, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        return new ImportResult(staged.Count + CountFailedRows(errors), successRows, CountFailedRows(errors), errors, stopwatch.Elapsed);
    }

    /// <summary>统计错误行数（同一行多列错误只计一次）。</summary>
    /// <param name="errors">错误明细。</param>
    /// <returns>去重后的错误行数。</returns>
    private static int CountFailedRows(List<ImportRowError> errors)
        => errors.Select(static e => e.RowNumber).Distinct().Count();

    /// <summary>
    /// 表头校验与列映射（列名匹配、顺序容错）：显示名 → 列索引。
    /// 未知列 / 缺失必填列 / 重复列均记入表头级错误（行号 1）。
    /// </summary>
    /// <param name="worksheet">导入工作表。</param>
    /// <param name="definition">模板定义。</param>
    /// <param name="errors">错误收集器。</param>
    /// <returns>列索引 → 列定义映射（表头行缺失时为空映射）。</returns>
    private static Dictionary<int, ImportColumnDefinition> MatchHeaders(
        IXLWorksheet worksheet, ImportTemplateDefinition definition, List<ImportRowError> errors)
    {
        var headerRow = worksheet.FirstRowUsed();
        if (headerRow is null)
        {
            errors.Add(new ImportRowError(1, string.Empty, "导入文件为空（无表头行）。"));
            return [];
        }

        var columnsByDisplayName = definition.Columns
            .GroupBy(static c => c.DisplayName.Trim(), StringComparer.Ordinal)
            .ToDictionary(static g => g.Key, static g => g.First(), StringComparer.Ordinal);

        var mapping = new Dictionary<int, ImportColumnDefinition>();
        var matchedColumns = new HashSet<string>(StringComparer.Ordinal);
        var lastColumn = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;
        for (var columnIndex = 1; columnIndex <= lastColumn; columnIndex++)
        {
            var header = worksheet.Cell(1, columnIndex).GetString().Trim();
            if (header.Length == 0)
            {
                continue;
            }

            if (!columnsByDisplayName.TryGetValue(header, out var column))
            {
                errors.Add(new ImportRowError(1, header, $"未知列「{header}」，请使用标准模板表头。"));
                continue;
            }

            if (!matchedColumns.Add(header))
            {
                errors.Add(new ImportRowError(1, header, $"列「{header}」在表头中重复出现。"));
                continue;
            }

            mapping[columnIndex] = column;
        }

        // 缺失必填列整批拒绝（列顺序容错，仅按显示名匹配）
        foreach (var column in definition.Columns.Where(static c => c.IsRequired))
        {
            if (!matchedColumns.Contains(column.DisplayName.Trim()))
            {
                errors.Add(new ImportRowError(1, column.ColumnName, $"必填列「{column.DisplayName}」缺失。"));
            }
        }

        return errors.Count == 0 ? mapping : [];
    }

    /// <summary>载入并规范化 xlsx 工作簿（非可 seek 流先缓冲为内存流）。</summary>
    /// <param name="stream">输入流。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>ClosedXML 工作簿。</returns>
    private static async Task<XLWorkbook> LoadWorkbookAsync(Stream stream, CancellationToken cancellationToken)
    {
        MemoryStream buffered;
        if (stream is MemoryStream memory && memory.CanSeek)
        {
            buffered = memory;
        }
        else
        {
            buffered = new MemoryStream();
            await stream.CopyToAsync(buffered, cancellationToken).ConfigureAwait(false);
            buffered.Position = 0;
        }

        try
        {
            return new XLWorkbook(buffered);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ConflictException($"导入文件解析失败：请确认上传的是有效的 xlsx 文件。（{ex.Message}）", "IMPORT_FILE_INVALID");
        }
    }
}
