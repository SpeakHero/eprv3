/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 15:00:00 +08:00 中国标准时间 星期五
 * 描述：导入导出服务单元测试——模板生成（数据页 + 说明页）、表头校验（未知列 / 重复列 / 缺失必填列整批拒绝）、
 *       行级校验（必填 / 数值格式 / 字典编码批量校验）、错误行明细（行号 + 列名 + 原因）、正常导入落点、导出投影
 */

using ClosedXML.Excel;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.ImportExport;
using Qlx.BuildingBlocks.ImportExport;

namespace Qlx.BuildingBlocks.Tests.ImportExport;

/// <summary>
/// <see cref="ImportExportService"/> 单元测试（真实 ClosedXML 工作簿 + 内存注册表 / 落点 / 数据源 / 字典校验器桩）。
/// </summary>
public sealed class ImportExportServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static ImportTemplateDefinition CreateDefinition() => new(
        "imp.test", "测试导入模板", "test",
        [
            new ImportColumnDefinition("name", "名称", true, ImportColumnDataTypes.Text, MaxLength: 10, ExampleValue: "示例商品"),
            new ImportColumnDefinition("qty", "数量", true, ImportColumnDataTypes.Number),
            new ImportColumnDefinition("cat", "类别", false, ImportColumnDataTypes.Text, DictionaryCode: "cat_dict"),
        ]);

    /// <summary>固定模板注册表桩。</summary>
    private sealed class StaticRegistry(params ImportTemplateDefinition[] definitions) : IImportTemplateRegistry
    {
        public ImportTemplateDefinition? Find(string templateCode)
            => definitions.FirstOrDefault(d => d.TemplateCode == templateCode);
    }

    /// <summary>记录落点行的收集桩。</summary>
    private sealed class CollectingSink(string templateCode) : IImportRowSink
    {
        public List<IReadOnlyDictionary<string, object?>> Rows { get; } = [];

        public string TemplateCode { get; } = templateCode;

        public Task<int> WriteRowsAsync(
            IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, Guid tenantId, CancellationToken cancellationToken)
        {
            Assert.Equal(TenantId, tenantId);
            Rows.AddRange(rows);
            return Task.FromResult(rows.Count);
        }
    }

    /// <summary>固定数据源桩。</summary>
    private sealed class StaticProvider(string templateCode, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) : IExportDataProvider
    {
        public string TemplateCode { get; } = templateCode;

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(
            IReadOnlyDictionary<string, object?> criteria, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(rows);
    }

    /// <summary>字典校验桩：批量记录调用，validValues 之外一律非法。</summary>
    private sealed class StubDictionaryValidator(params string[] validValues) : IImportDictionaryValidator
    {
        public List<(string DictionaryCode, IReadOnlyCollection<string> Values)> Calls { get; } = [];

        public Task<IReadOnlyDictionary<string, bool>> ValidateAsync(
            string dictionaryCode, IReadOnlyCollection<string> values, CancellationToken cancellationToken)
        {
            Calls.Add((dictionaryCode, values));
            return Task.FromResult<IReadOnlyDictionary<string, bool>>(
                values.ToDictionary(static v => v, v => validValues.Contains(v)));
        }
    }

    /// <summary>构造内存 xlsx 流（首行表头 + 数据行）。</summary>
    private static MemoryStream CreateExcelStream(string[] headers, params IReadOnlyList<string?[]> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(ImportExportService.DataSheetName);
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
        }

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                if (rows[r][c] is { } value)
                {
                    sheet.Cell(r + 2, c + 1).Value = value;
                }
            }
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static ImportExportService BuildService(
        CollectingSink? sink = null,
        IImportDictionaryValidator? validator = null,
        IExportDataProvider? provider = null,
        ImportTemplateDefinition? definition = null)
    {
        var actualDefinition = definition ?? CreateDefinition();
        return new ImportExportService(
            new StaticRegistry(actualDefinition),
            sink is null ? [] : [sink],
            provider is null ? [] : [provider],
            validator);
    }

    // ==================== 模板生成 ====================

    [Fact]
    public async Task GenerateTemplateAsyncCreatesDataAndGuideSheets()
    {
        var service = BuildService();

        var bytes = await service.GenerateTemplateAsync("imp.test", TestContext.Current.CancellationToken);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var dataSheet = workbook.Worksheet(ImportExportService.DataSheetName);
        Assert.Equal("名称", dataSheet.Cell(1, 1).GetString());
        Assert.Equal("数量", dataSheet.Cell(1, 2).GetString());
        Assert.Equal("类别", dataSheet.Cell(1, 3).GetString());
        Assert.Equal("示例商品", dataSheet.Cell(2, 1).GetString());

        var guideSheet = workbook.Worksheet(ImportExportService.GuideSheetName);
        Assert.Equal("name", guideSheet.Cell(2, 1).GetString());
        Assert.Equal("名称", guideSheet.Cell(2, 2).GetString());
        Assert.Equal("是", guideSheet.Cell(2, 3).GetString());
        Assert.Equal("cat_dict", guideSheet.Cell(4, 6).GetString());
    }

    [Fact]
    public async Task GenerateTemplateAsyncUnknownTemplateThrowsNotFound()
    {
        var service = BuildService();

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.GenerateTemplateAsync("imp.none", TestContext.Current.CancellationToken));
    }

    // ==================== 导入：模板与文件级 ====================

    [Fact]
    public async Task ImportAsyncUnknownTemplateThrowsNotFound()
    {
        var service = BuildService();
        using var stream = CreateExcelStream(["名称", "数量"], ["商品A", "3"]);

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.ImportAsync(stream, "imp.none", TenantId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImportAsyncMissingSinkThrowsNotFound()
    {
        var service = BuildService(sink: null);
        using var stream = CreateExcelStream(["名称", "数量"], ["商品A", "3"]);

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.ImportAsync(stream, "imp.test", TenantId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImportAsyncEmptyBytesThrowsFileInvalid()
    {
        var service = BuildService(sink: new CollectingSink("imp.test"));
        using var stream = new MemoryStream([]);

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => service.ImportAsync(stream, "imp.test", TenantId, TestContext.Current.CancellationToken));
        Assert.Equal("IMPORT_FILE_INVALID", exception.ErrorCode);
    }

    // ==================== 导入：表头级校验（整批拒绝） ====================

    [Fact]
    public async Task ImportAsyncUnknownAndMissingHeadersRejectWholeBatchWithRowNumberOne()
    {
        var service = BuildService(sink: new CollectingSink("imp.test"));
        using var stream = CreateExcelStream(["名字", "数量"], ["商品A", "3"]);

        var result = await service.ImportAsync(stream, "imp.test", TenantId, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.TotalRows);
        Assert.Equal(0, result.SuccessRows);
        Assert.All(result.Errors, static e => Assert.Equal(1, e.RowNumber));
        Assert.Contains(result.Errors, static e => e.ErrorMessage.Contains("未知列"));
        Assert.Contains(result.Errors, static e => e.ColumnName == "name");
    }

    // ==================== 导入：行级校验与错误明细 ====================

    [Fact]
    public async Task ImportAsyncRequiredAndFormatErrorsRecordedWithRowAndColumn()
    {
        var service = BuildService(sink: new CollectingSink("imp.test"));
        // 行 2：必填名称为空；行 3：数量非数值
        using var stream = CreateExcelStream(
            ["名称", "数量", "类别"],
            ["", "3", ""],
            ["商品B", "abc", ""]);

        var result = await service.ImportAsync(stream, "imp.test", TenantId, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.FailedRows);
        Assert.Equal(0, result.SuccessRows);
        Assert.Contains(result.Errors, static e => e.RowNumber == 2 && e.ColumnName == "name");
        Assert.Contains(result.Errors, static e => e.RowNumber == 3 && e.ColumnName == "qty" && e.ErrorMessage.Contains("数值"));
    }

    [Fact]
    public async Task ImportAsyncOverLengthValueRecordedAsError()
    {
        var service = BuildService(sink: new CollectingSink("imp.test"));
        using var stream = CreateExcelStream(
            ["名称", "数量"],
            ["超长商品名称超长商品名称超长", "1"]);

        var result = await service.ImportAsync(stream, "imp.test", TenantId, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.FailedRows);
        Assert.Contains(result.Errors, static e => e.ColumnName == "name" && e.ErrorMessage.Contains("长度超限"));
    }

    [Fact]
    public async Task ImportAsyncDictionaryValuesBatchValidatedAndInvalidRowsExcluded()
    {
        var sink = new CollectingSink("imp.test");
        var validator = new StubDictionaryValidator("A1");
        var service = BuildService(sink: sink, validator: validator);
        using var stream = CreateExcelStream(
            ["名称", "数量", "类别"],
            ["商品A", "1", "A1"],
            ["商品B", "2", "X9"]);

        var result = await service.ImportAsync(stream, "imp.test", TenantId, TestContext.Current.CancellationToken);

        // 同一字典编码一次批量调用（候选值去重），非法行剔除且不出现在落点
        var call = Assert.Single(validator.Calls);
        Assert.Equal("cat_dict", call.DictionaryCode);
        Assert.Equal(["A1", "X9"], [.. call.Values]);
        Assert.Equal(1, result.SuccessRows);
        Assert.Equal(1, result.FailedRows);
        var error = Assert.Single(result.Errors);
        Assert.Equal(3, error.RowNumber);
        Assert.Equal("cat", error.ColumnName);
        var row = Assert.Single(sink.Rows);
        Assert.Equal("A1", row["cat"]);
    }

    [Fact]
    public async Task ImportAsyncDictionaryColumnWithoutValidatorThrowsConflict()
    {
        var service = BuildService(sink: new CollectingSink("imp.test"), validator: null);
        using var stream = CreateExcelStream(
            ["名称", "数量", "类别"],
            ["商品A", "1", "A1"]);

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => service.ImportAsync(stream, "imp.test", TenantId, TestContext.Current.CancellationToken));
        Assert.Equal("IMPORT_DICTIONARY_VALIDATOR_MISSING", exception.ErrorCode);
    }

    [Fact]
    public async Task ImportAsyncValidRowsPassedToSinkWithCounts()
    {
        var sink = new CollectingSink("imp.test");
        var validator = new StubDictionaryValidator("A1", "B2");
        var service = BuildService(sink: sink, validator: validator);
        using var stream = CreateExcelStream(
            ["类别", "数量", "名称"],
            ["A1", "1", "商品A"],
            ["B2", "2", "商品B"],
            ["", "", ""]);

        var result = await service.ImportAsync(stream, "imp.test", TenantId, TestContext.Current.CancellationToken);

        // 列顺序容错；尾部空行不计入总行数
        Assert.Equal(2, result.TotalRows);
        Assert.Equal(2, result.SuccessRows);
        Assert.Equal(0, result.FailedRows);
        Assert.Equal(2, sink.Rows.Count);
        Assert.Equal("商品A", sink.Rows[0]["name"]);
        Assert.Equal(1m, sink.Rows[0]["qty"]);
        Assert.Equal(2m, sink.Rows[1]["qty"]);
    }

    // ==================== 导出投影 ====================

    [Fact]
    public async Task ExportAsyncProviderRowsProjectedToWorkbook()
    {
        var provider = new StaticProvider("imp.test",
        [
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = "商品A",
                ["qty"] = 3m,
                ["cat"] = "A1",
            },
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = "商品B",
                ["qty"] = 5m,
                ["cat"] = null,
            },
        ]);
        var service = BuildService(provider: provider);

        var result = await service.ExportAsync("imp.test", new Dictionary<string, object?>(), TenantId, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.RowCount);
        Assert.Equal(ImportExportService.XlsxContentType, result.ContentType);
        Assert.StartsWith("imp.test-", result.FileName);
        Assert.EndsWith(".xlsx", result.FileName);
        using var workbook = new XLWorkbook(result.FileStream);
        var sheet = workbook.Worksheets.First();
        Assert.Equal("名称", sheet.Cell(1, 1).GetString());
        Assert.Equal("数量", sheet.Cell(1, 2).GetString());
        Assert.Equal("商品A", sheet.Cell(2, 1).GetString());
        Assert.Equal(3m, (decimal)sheet.Cell(2, 2).GetDouble());
        Assert.Equal("商品B", sheet.Cell(3, 1).GetString());
    }

    [Fact]
    public async Task ExportAsyncUnknownTemplateThrowsNotFound()
    {
        var service = BuildService(provider: new StaticProvider("imp.none", []));

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.ExportAsync("imp.none", new Dictionary<string, object?>(), TenantId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExportAsyncMissingProviderThrowsNotFound()
    {
        var service = BuildService(provider: null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.ExportAsync("imp.test", new Dictionary<string, object?>(), TenantId, TestContext.Current.CancellationToken));
    }
}
