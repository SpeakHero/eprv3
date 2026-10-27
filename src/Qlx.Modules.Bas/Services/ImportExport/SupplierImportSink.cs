/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 01:20:00 +08:00 中国标准时间 星期一
 * 描述：供应商导入落点——内存行级校验（含行号）+ 文件内与库内统一社会信用代码批量防重 +
 *       统一发号器编码 + 单次批量保存
 */

using System.Globalization;

using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.ImportExport;
using Qlx.Modules.Bas.Domain.Supplier;
using Qlx.Modules.Bas.Repositories;
using Qlx.Modules.Bas.Services.Numbering;

namespace Qlx.Modules.Bas.Services.ImportExport;

/// <summary>
/// 供应商导入行落点（模板 <c>bas.supplier.import</c>）。行级校验全部在内存完成，
/// 统一社会信用代码经一次批量 IN 查询防重后逐条发号，最终单次 <c>SaveChanges</c>（禁逐行往返）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文（交叉校验导入租户）。</param>
/// <param name="repo">Bas 租户仓储。</param>
/// <param name="codeNumberGenerator">统一发号器（供应商编码）。</param>
internal sealed class SupplierImportSink(
    ITenantContext tenantCtx,
    IBasTenantRepository repo,
    IBasCodeNumberGenerator codeNumberGenerator) : IImportRowSink
{
    private const string CodeBizType = "supplier";

    /// <inheritdoc />
    public string TemplateCode => BasImportTemplateDefinitionProvider.SupplierImport;

    /// <inheritdoc />
    public async Task<int> WriteRowsAsync(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        long currentTenantId = tenantCtx.TenantId
            ?? throw new ForbiddenException("未识别到租户上下文");
        long restoredTenantId = ImportRowValues.ResolveTenantId(tenantId, currentTenantId);

        List<FieldError> errors = [];
        List<ParsedRow> parsed = ParseRows(rows, errors);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        // 库内已存在的统一社会信用代码：一次批量 IN 查询（禁逐行往返）
        IReadOnlyCollection<string> existing = await repo
            .ListExistingSupplierUsccsAsync([.. parsed.Select(static r => r.Uscc)], cancellationToken)
            .ConfigureAwait(false);
        if (existing.Count > 0)
        {
            foreach (ParsedRow row in parsed.Where(r => existing.Contains(r.Uscc)))
            {
                errors.Add(new FieldError(
                    "uscc", $"第 {row.RowNumber.ToString(CultureInfo.InvariantCulture)} 行：统一社会信用代码已存在（{row.Uscc}）"));
            }

            throw new ValidationException(errors);
        }

        foreach (ParsedRow row in parsed)
        {
            string code = await codeNumberGenerator.NextAsync(
                CodeBizType,
                restoredTenantId,
                parentCode: null,
                typeCode: null,
                isConflict: (candidate, token) => repo.IsSupplierCodeExistsAsync(candidate, token),
                ct: cancellationToken).ConfigureAwait(false);

            Supplier supplier = new(restoredTenantId, code, row.Name, row.Uscc, row.SupplierType);
            supplier.UpdateProfile(row.Name, row.Uscc, row.SupplierType, row.Contact, row.Phone);
            await repo.AddSupplierAsync(supplier, cancellationToken).ConfigureAwait(false);
        }

        _ = await repo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return parsed.Count;
    }

    private static List<ParsedRow> ParseRows(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, List<FieldError> errors)
    {
        List<ParsedRow> parsed = [];
        Dictionary<string, int> firstRowByUscc = new(StringComparer.Ordinal);

        for (int index = 0; index < rows.Count; index++)
        {
            IReadOnlyDictionary<string, object?> row = rows[index];
            int rowNumber = ImportRowValues.RowNumber(index);

            string? name = ImportRowValues.ReadString(row, "name");
            if (name is null)
            {
                errors.Add(new FieldError("name", RowMessage(rowNumber, "列「供应商名称」不能为空")));
            }

            string? uscc = ImportRowValues.ReadString(row, "uscc");
            if (uscc is null)
            {
                errors.Add(new FieldError("uscc", RowMessage(rowNumber, "列「统一社会信用代码」不能为空")));
            }
            else if (firstRowByUscc.TryGetValue(uscc, out int firstRow))
            {
                errors.Add(new FieldError(
                    "uscc",
                    RowMessage(
                        rowNumber,
                        $"统一社会信用代码与第 {firstRow.ToString(CultureInfo.InvariantCulture)} 行重复（{uscc}）")));
            }
            else
            {
                firstRowByUscc[uscc] = rowNumber;
            }

            SupplierType? supplierType = ParseSupplierType(ImportRowValues.ReadString(row, "supplierType"));
            if (supplierType is null)
            {
                errors.Add(new FieldError("supplierType", RowMessage(rowNumber, "列「供应商类型」不是有效取值（1~5）")));
            }

            if (name is null || uscc is null || supplierType is null)
            {
                continue;
            }

            parsed.Add(new ParsedRow(
                rowNumber,
                name,
                uscc,
                supplierType.Value,
                ImportRowValues.ReadString(row, "contact"),
                ImportRowValues.ReadString(row, "phone")));
        }

        return parsed;
    }

    private static SupplierType? ParseSupplierType(string? text)
    {
        if (text is null
            || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            || !Enum.IsDefined(typeof(SupplierType), value))
        {
            return null;
        }

        return (SupplierType)value;
    }

    private static string RowMessage(int rowNumber, string message)
        => $"第 {rowNumber.ToString(CultureInfo.InvariantCulture)} 行：{message}";

    private sealed record ParsedRow(
        int RowNumber,
        string Name,
        string Uscc,
        SupplierType SupplierType,
        string? Contact,
        string? Phone);
}
