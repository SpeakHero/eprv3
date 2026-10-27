/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 01:40:00 +08:00 中国标准时间 星期一
 * 描述：商品分类导入落点——内存行级校验（含行号）+ 父分类/计量单位组批量引用校验 +
 *       层级连续编码（{parent}{seq:2}）单次批量保存
 */

using System.Globalization;

using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.ImportExport;
using Qlx.Modules.Bas.Domain.Goods;
using Qlx.Modules.Bas.Repositories;
using Qlx.Modules.Bas.Services.Numbering;

namespace Qlx.Modules.Bas.Services.ImportExport;

/// <summary>
/// 商品分类导入行落点（模板 <c>bas.category.import</c>）。父分类按上级编码解析，
/// 分类编码由统一发号器按 <c>{parent}{seq:2}</c> 规则生成，层级为父层级 + 1；
/// 行级校验全部在内存完成，最终单次 <c>SaveChanges</c>（禁逐行往返）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文（交叉校验导入租户）。</param>
/// <param name="repo">Bas 租户仓储。</param>
/// <param name="codeNumberGenerator">统一发号器（分类编码）。</param>
internal sealed class CategoryImportSink(
    ITenantContext tenantCtx,
    IBasTenantRepository repo,
    IBasCodeNumberGenerator codeNumberGenerator) : IImportRowSink
{
    private const string CodeBizType = "category";

    /// <inheritdoc />
    public string TemplateCode => BasImportTemplateDefinitionProvider.CategoryImport;

    /// <inheritdoc />
    public async Task<int> WriteRowsAsync(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        long currentTenantId = tenantCtx.TenantId
            ?? throw new ForbiddenException("未识别到租户上下文");
        long restoredTenantId = ImportRowValues.ResolveTenantId(tenantId, currentTenantId);

        // 父分类与计量单位组各一次批量查询（禁逐行往返）
        IReadOnlyList<ItemCategory> existingCategories = await repo
            .ListAllCategoriesAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, ItemCategory> categoryByCode = new(StringComparer.Ordinal);
        foreach (ItemCategory category in existingCategories)
        {
            categoryByCode[category.Code] = category;
        }

        HashSet<long> unitGroupIds = [.. (await repo.ListAllUnitGroupsAsync(cancellationToken)
            .ConfigureAwait(false)).Select(static g => g.Id)];

        List<FieldError> errors = [];
        List<ParsedRow> parsed = ParseRows(rows, categoryByCode, unitGroupIds, errors);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        foreach (ParsedRow row in parsed)
        {
            string code = await codeNumberGenerator.NextAsync(
                CodeBizType,
                restoredTenantId,
                parentCode: row.Parent?.Code,
                typeCode: null,
                isConflict: (candidate, token) => repo.IsCategoryCodeExistsAsync(candidate, token),
                ct: cancellationToken).ConfigureAwait(false);

            ItemCategory created = new(
                restoredTenantId,
                row.Name,
                row.Parent?.Id,
                row.Parent is null ? 1 : row.Parent.LevelNo + 1,
                code,
                row.UnitGroupId);
            await repo.AddCategoryAsync(created, cancellationToken).ConfigureAwait(false);
        }

        _ = await repo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return parsed.Count;
    }

    private static List<ParsedRow> ParseRows(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        Dictionary<string, ItemCategory> categoryByCode,
        HashSet<long> unitGroupIds,
        List<FieldError> errors)
    {
        List<ParsedRow> parsed = [];

        for (int index = 0; index < rows.Count; index++)
        {
            IReadOnlyDictionary<string, object?> row = rows[index];
            int rowNumber = ImportRowValues.RowNumber(index);

            string? name = ImportRowValues.ReadString(row, "name");
            if (name is null)
            {
                errors.Add(new FieldError("name", RowMessage(rowNumber, "列「分类名称」不能为空")));
            }

            ItemCategory? parent = null;
            string? parentCode = ImportRowValues.ReadString(row, "parentCode");
            if (parentCode is not null && !categoryByCode.TryGetValue(parentCode, out parent))
            {
                errors.Add(new FieldError(
                    "parentCode", RowMessage(rowNumber, $"上级分类编码不存在（{parentCode}）")));
            }

            long? unitGroupId = ParseId(ImportRowValues.ReadString(row, "unitGroupId"));
            if (ImportRowValues.ReadString(row, "unitGroupId") is not null && unitGroupId is null)
            {
                errors.Add(new FieldError("unitGroupId", RowMessage(rowNumber, "列「计量单位组Id」不是有效的正整数")));
            }
            else if (unitGroupId.HasValue && !unitGroupIds.Contains(unitGroupId.Value))
            {
                errors.Add(new FieldError("unitGroupId", RowMessage(rowNumber, "计量单位组不存在")));
            }

            if (name is null)
            {
                continue;
            }

            parsed.Add(new ParsedRow(rowNumber, name, parent, unitGroupId));
        }

        return parsed;
    }

    private static long? ParseId(string? text)
        => text is not null
           && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
           && value > 0
            ? value
            : null;

    private static string RowMessage(int rowNumber, string message)
        => $"第 {rowNumber.ToString(CultureInfo.InvariantCulture)} 行：{message}";

    private sealed record ParsedRow(
        int RowNumber,
        string Name,
        ItemCategory? Parent,
        long? UnitGroupId);
}
