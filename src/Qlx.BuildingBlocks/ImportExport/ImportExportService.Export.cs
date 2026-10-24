/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 12:00:00 +08:00 中国标准时间 星期五
 * 描述：导出投影与导入模板生成——数据页按列元数据投影 + 说明页元数据清单
 */

using System.Diagnostics;
using ClosedXML.Excel;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.ImportExport;

namespace Qlx.BuildingBlocks.ImportExport;

/// <summary>
/// <see cref="ImportExportService"/> 的导出与模板生成部分。
/// </summary>
public sealed partial class ImportExportService
{
    /// <summary>说明页表头（列元数据清单）。</summary>
    private static readonly string[] s_guideHeaders =
        ["字段名", "表头显示名", "是否必填", "数据类型", "最大长度", "字典编码", "示例值", "说明"];

    /// <summary>
    /// 生成标准导入模板 xlsx：「数据」页（表头 + 示例行）+「说明」页（列元数据清单）。
    /// </summary>
    /// <param name="templateCode">模板编码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>模板文件字节数组。</returns>
    /// <exception cref="NotFoundException">模板未注册。</exception>
    public async Task<byte[]> GenerateTemplateAsync(string templateCode, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateCode);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        var definition = _registry.Find(templateCode) ?? throw new NotFoundException("导入模板", templateCode);

        using var workbook = new XLWorkbook();
        var dataSheet = workbook.Worksheets.Add(DataSheetName);
        WriteHeaderRow(dataSheet, definition);
        WriteExampleRow(dataSheet, definition);

        var guideSheet = workbook.Worksheets.Add(GuideSheetName);
        WriteGuideSheet(guideSheet, definition);

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    /// <inheritdoc />
    public async Task<ExportResult> ExportAsync(string templateCode, IReadOnlyDictionary<string, object?> criteria, Guid tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateCode);
        ArgumentNullException.ThrowIfNull(criteria);

        var definition = _registry.Find(templateCode) ?? throw new NotFoundException("导出模板", templateCode);
        var provider = _dataProviders.FirstOrDefault(p => string.Equals(p.TemplateCode, templateCode, StringComparison.Ordinal))
            ?? throw new NotFoundException("模板导出数据源（IExportDataProvider）", templateCode);

        var stopwatch = Stopwatch.StartNew();
        var rows = await provider.GetRowsAsync(criteria, tenantId, cancellationToken).ConfigureAwait(false);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(DataSheetName);
        WriteHeaderRow(sheet, definition);

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            for (var columnIndex = 0; columnIndex < definition.Columns.Count; columnIndex++)
            {
                var column = definition.Columns[columnIndex];
                WriteCell(sheet.Cell(rowIndex + 2, columnIndex + 1), row.GetValueOrDefault(column.ColumnName));
            }
        }

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        stopwatch.Stop();

        var stream = new MemoryStream(output.ToArray()) { Position = 0 };
        return new ExportResult(
            stream,
            $"{templateCode}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.xlsx",
            XlsxContentType,
            rows.Count,
            stopwatch.Elapsed);
    }

    /// <summary>写表头行（显示名 + 必填列黄色底纹标识，顺序 = 列定义顺序）。</summary>
    private static void WriteHeaderRow(IXLWorksheet sheet, ImportTemplateDefinition definition)
    {
        for (var columnIndex = 0; columnIndex < definition.Columns.Count; columnIndex++)
        {
            var column = definition.Columns[columnIndex];
            var headerCell = sheet.Cell(1, columnIndex + 1);
            headerCell.Value = column.DisplayName;
            headerCell.Style.Font.Bold = true;
            if (column.IsRequired)
            {
                headerCell.Style.Fill.BackgroundColor = XLColor.LightYellow;
            }
        }
    }

    /// <summary>写示例行（示例值，斜体灰字提示用户替换）。</summary>
    private static void WriteExampleRow(IXLWorksheet sheet, ImportTemplateDefinition definition)
    {
        for (var columnIndex = 0; columnIndex < definition.Columns.Count; columnIndex++)
        {
            var column = definition.Columns[columnIndex];
            if (column.ExampleValue is not { } example)
            {
                continue;
            }

            var cell = sheet.Cell(2, columnIndex + 1);
            cell.Value = example;
            cell.Style.Font.Italic = true;
            cell.Style.Font.FontColor = XLColor.Gray;
        }
    }

    /// <summary>写「说明」页：全部列元数据清单（字段名/显示名/必填/类型/长度/字典/示例/说明）。</summary>
    private static void WriteGuideSheet(IXLWorksheet sheet, ImportTemplateDefinition definition)
    {
        for (var headerIndex = 0; headerIndex < s_guideHeaders.Length; headerIndex++)
        {
            var headerCell = sheet.Cell(1, headerIndex + 1);
            headerCell.Value = s_guideHeaders[headerIndex];
            headerCell.Style.Font.Bold = true;
        }

        for (var rowIndex = 0; rowIndex < definition.Columns.Count; rowIndex++)
        {
            var column = definition.Columns[rowIndex];
            var rowNumber = rowIndex + 2;
            sheet.Cell(rowNumber, 1).Value = column.ColumnName;
            sheet.Cell(rowNumber, 2).Value = column.DisplayName;
            sheet.Cell(rowNumber, 3).Value = column.IsRequired ? "是" : "否";
            sheet.Cell(rowNumber, 4).Value = column.DataType ?? ImportColumnDataTypes.Text;
            sheet.Cell(rowNumber, 5).Value = column.MaxLength?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "不限";
            sheet.Cell(rowNumber, 6).Value = column.DictionaryCode ?? string.Empty;
            sheet.Cell(rowNumber, 7).Value = column.ExampleValue ?? string.Empty;
            sheet.Cell(rowNumber, 8).Value = column.Description ?? string.Empty;
        }

        sheet.Columns().AdjustToContents();
    }

    /// <summary>按值类型写入单元格（日期列写原生日期便于 Excel 排序，其余字符串化）。</summary>
    private static void WriteCell(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                cell.Value = string.Empty;
                break;
            case string text:
                cell.Value = text;
                break;
            case DateTime dateTime:
                cell.Value = dateTime;
                break;
            case DateOnly date:
                cell.Value = date.ToDateTime(TimeOnly.MinValue);
                break;
            case bool flag:
                cell.Value = flag ? "是" : "否";
                break;
            case decimal number:
                cell.Value = number;
                break;
            case int intValue:
                cell.Value = intValue;
                break;
            case long longValue:
                cell.Value = longValue;
                break;
            default:
                cell.Value = value.ToString() ?? string.Empty;
                break;
        }
    }
}
