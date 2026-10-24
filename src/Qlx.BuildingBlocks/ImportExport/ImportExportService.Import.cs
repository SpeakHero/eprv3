/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 12:00:00 +08:00 中国标准时间 星期五
 * 描述：导入行级解析与校验——必填 / 类型 / 长度 / 字典编码（分批校验），错误行明细收集
 */

using System.Globalization;
using ClosedXML.Excel;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.ImportExport;

namespace Qlx.BuildingBlocks.ImportExport;

/// <summary>导入行解析中间态（行号 + 已解析字段值）。</summary>
/// <param name="RowNumber">Excel 行号（自 1 起，表头行为 1）。</param>
/// <param name="Values">字段值（键 = 列字段名）。</param>
/// <param name="PendingDictionaryChecks">待字典校验的取值（字典编码 → 值），仅暂存不落点。</param>
internal sealed record ImportStagedRow(
    int RowNumber,
    Dictionary<string, object?> Values,
    List<(string DictionaryCode, string ColumnName, string Value)> PendingDictionaryChecks);

/// <summary>
/// <see cref="ImportExportService"/> 的行级解析与校验部分。
/// </summary>
public sealed partial class ImportExportService
{
    /// <summary>布尔列的中文合法取值（是/否）。</summary>
    private static readonly string[] s_trueText = ["true", "是", "1"];
    private static readonly string[] s_falseText = ["false", "否", "0"];

    /// <summary>
    /// 逐行解析数据行：必填 / 类型 / 长度校验，通过行进入暂存，错误写入明细（同一行聚合多列错误）。
    /// </summary>
    /// <param name="worksheet">导入工作表。</param>
    /// <param name="definition">模板定义。</param>
    /// <param name="columnsByIndex">列索引 → 列定义映射。</param>
    /// <param name="errors">错误收集器。</param>
    /// <returns>通过基础校验的暂存行集合。</returns>
    private static List<ImportStagedRow> ParseDataRows(
        IXLWorksheet worksheet,
        ImportTemplateDefinition definition,
        IReadOnlyDictionary<int, ImportColumnDefinition> columnsByIndex,
        List<ImportRowError> errors)
    {
        var staged = new List<ImportStagedRow>();
        var headerRowNumber = worksheet.FirstRowUsed()?.RowNumber() ?? 1;
        var lastRowNumber = worksheet.LastRowUsed()?.RowNumber() ?? headerRowNumber;

        for (var rowNumber = headerRowNumber + 1; rowNumber <= lastRowNumber; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            if (row.IsEmpty())
            {
                // 尾部空行静默跳过，不计入总行数
                continue;
            }

            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            var pendingChecks = new List<(string DictionaryCode, string ColumnName, string Value)>();
            var rowErrors = new List<ImportRowError>();

            foreach (var (columnIndex, column) in columnsByIndex)
            {
                var text = ReadCellText(worksheet.Cell(rowNumber, columnIndex));
                ParseCell(rowNumber, column, text, values, pendingChecks, rowErrors);
            }

            if (rowErrors.Count > 0)
            {
                errors.AddRange(rowErrors);
            }
            else
            {
                staged.Add(new ImportStagedRow(rowNumber, values, pendingChecks));
            }
        }

        return staged;
    }

    /// <summary>解析单元格取值：必填 / 类型 / 长度校验，通过后写入行值或字典待校验清单。</summary>
    private static void ParseCell(
        int rowNumber,
        ImportColumnDefinition column,
        string text,
        Dictionary<string, object?> values,
        List<(string DictionaryCode, string ColumnName, string Value)> pendingChecks,
        List<ImportRowError> rowErrors)
    {
        if (text.Length == 0)
        {
            if (column.IsRequired)
            {
                rowErrors.Add(new ImportRowError(rowNumber, column.ColumnName, $"必填列「{column.DisplayName}」不能为空。"));
            }
            else
            {
                values[column.ColumnName] = null;
            }

            return;
        }

        switch (column.DataType ?? ImportColumnDataTypes.Text)
        {
            case ImportColumnDataTypes.Number:
            {
                if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    rowErrors.Add(new ImportRowError(rowNumber, column.ColumnName, $"列「{column.DisplayName}」应为数值，实际值：{text}。"));
                    return;
                }

                values[column.ColumnName] = number;
                return;
            }

            case ImportColumnDataTypes.Date:
            {
                if (!TryParseDate(text, out var date))
                {
                    rowErrors.Add(new ImportRowError(rowNumber, column.ColumnName, $"列「{column.DisplayName}」应为日期，实际值：{text}。"));
                    return;
                }

                values[column.ColumnName] = date;
                return;
            }

            case ImportColumnDataTypes.Boolean:
            {
                if (!TryParseBoolean(text, out var flag))
                {
                    rowErrors.Add(new ImportRowError(rowNumber, column.ColumnName, $"列「{column.DisplayName}」应为布尔（true/false/是/否），实际值：{text}。"));
                    return;
                }

                values[column.ColumnName] = flag;
                return;
            }

            default:
                if (column.MaxLength is { } maxLength && text.Length > maxLength)
                {
                    rowErrors.Add(new ImportRowError(rowNumber, column.ColumnName, $"列「{column.DisplayName}」长度超限（最多 {maxLength} 字符，实际 {text.Length}）。"));
                    return;
                }

                if (column.DictionaryCode is { } dictCode)
                {
                    // 字典列先记录待校验值，统一分批校验（避免逐格往返）
                    pendingChecks.Add((dictCode, column.ColumnName, text));
                    return;
                }

                values[column.ColumnName] = text;
                return;
        }
    }

    /// <summary>
    /// 字典编码列分批校验：按字典编码分组、批量调用校验器，非法值转为错误明细并从暂存行剔除。
    /// </summary>
    /// <param name="definition">模板定义。</param>
    /// <param name="staged">暂存行。</param>
    /// <param name="errors">错误收集器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>字典校验通过后的暂存行。</returns>
    /// <exception cref="ConflictException">模板声明了字典列但未注册字典校验器。</exception>
    private async Task<List<ImportStagedRow>> ValidateDictionaryValuesAsync(
        ImportTemplateDefinition definition,
        List<ImportStagedRow> staged,
        List<ImportRowError> errors,
        CancellationToken cancellationToken)
    {
        var pendingRows = staged.Where(static r => r.PendingDictionaryChecks.Count > 0).ToList();
        if (pendingRows.Count == 0)
        {
            return staged;
        }

        if (_dictionaryValidator is null)
        {
            var dictCodes = pendingRows
                .SelectMany(static r => r.PendingDictionaryChecks)
                .Select(static c => c.DictionaryCode)
                .Distinct(StringComparer.Ordinal);
            throw new ConflictException(
                $"模板 {definition.TemplateCode} 声明了字典列（{string.Join(", ", dictCodes)}）但未注册字典校验器。",
                "IMPORT_DICTIONARY_VALIDATOR_MISSING");
        }

        // 同一字典编码只发一次批量请求（多行同列去重）
        var groupedChecks = pendingRows
            .SelectMany(static r => r.PendingDictionaryChecks.Select(c => (Row: r, Check: c)))
            .GroupBy(static x => x.Check.DictionaryCode, StringComparer.Ordinal);

        var invalidByRow = new Dictionary<int, List<ImportRowError>>();
        foreach (var group in groupedChecks)
        {
            var candidates = group.Select(static x => x.Check.Value).Distinct(StringComparer.Ordinal).ToList();
            var verdicts = await _dictionaryValidator
                .ValidateAsync(group.Key, candidates, cancellationToken).ConfigureAwait(false);

            foreach (var (row, check) in group)
            {
                if (verdicts.GetValueOrDefault(check.Value))
                {
                    row.Values[check.ColumnName] = check.Value;
                    continue;
                }

                if (!invalidByRow.TryGetValue(row.RowNumber, out var rowErrors))
                {
                    rowErrors = [];
                    invalidByRow[row.RowNumber] = rowErrors;
                }

                var displayName = definition.Columns
                    .First(c => string.Equals(c.ColumnName, check.ColumnName, StringComparison.Ordinal)).DisplayName;
                rowErrors.Add(new ImportRowError(row.RowNumber, check.ColumnName, $"列「{displayName}」取值 {check.Value} 不在字典 {group.Key} 范围内。"));
            }
        }

        if (invalidByRow.Count == 0)
        {
            return staged;
        }

        errors.AddRange(invalidByRow.Values.SelectMany(static e => e));
        return staged.Where(r => !invalidByRow.ContainsKey(r.RowNumber)).ToList();
    }

    /// <summary>读取单元格规范化文本（空白单元格返回空串，两端去空白）。</summary>
    private static string ReadCellText(IXLCell cell)
        => cell.DataType == XLDataType.Blank ? string.Empty : cell.GetString().Trim();

    /// <summary>尝试解析日期（ISO / 常见中英文格式）。</summary>
    private static bool TryParseDate(string text, out DateTime value)
        => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    /// <summary>尝试解析布尔（true/false/是/否/1/0）。</summary>
    private static bool TryParseBoolean(string text, out bool value)
    {
        if (s_trueText.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (s_falseText.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }
}
