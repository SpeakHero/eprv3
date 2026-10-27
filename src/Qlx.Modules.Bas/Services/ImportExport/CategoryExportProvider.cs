/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 01:50:00 +08:00 中国标准时间 星期一
 * 描述：商品分类导出数据源——单次全量查询 + 父级编码映射（扁平原始字段，不做树形组装）
 */

using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.ImportExport;
using Qlx.Modules.Bas.Domain.Goods;
using Qlx.Modules.Bas.Repositories;

namespace Qlx.Modules.Bas.Services.ImportExport;

/// <summary>
/// 商品分类导出数据源（模板 <c>bas.category.export</c>）。一次全量查询同时承担父级编码映射，
/// 仅下发扁平列表与上级编码原始字段（树形组装由前端完成，B-2-1）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文（交叉校验导出租户）。</param>
/// <param name="repo">Bas 租户仓储。</param>
internal sealed class CategoryExportProvider(
    ITenantContext tenantCtx,
    IBasTenantRepository repo) : IExportDataProvider
{
    /// <inheritdoc />
    public string TemplateCode => BasImportTemplateDefinitionProvider.CategoryExport;

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(
        IReadOnlyDictionary<string, object?> criteria,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        long currentTenantId = tenantCtx.TenantId
            ?? throw new ForbiddenException("未识别到租户上下文");
        long restoredTenantId = ImportRowValues.ResolveTenantId(tenantId, currentTenantId);

        IReadOnlyList<ItemCategory> categories = await repo
            .ListAllCategoriesAsync(cancellationToken).ConfigureAwait(false);

        Dictionary<long, string> codeById = new();
        foreach (ItemCategory category in categories.Where(c => c.TenantId == restoredTenantId))
        {
            codeById[category.Id] = category.Code;
        }

        int? status = ImportRowValues.CriteriaInt32(criteria, "status");
        string? keyword = ImportRowValues.CriteriaString(criteria, "keyword");

        return
        [
            .. categories
                .Where(c => c.TenantId == restoredTenantId)
                .Where(c => !status.HasValue || (int)c.Status == status.Value)
                .Where(c => keyword is null
                    || c.Name.Contains(keyword, StringComparison.Ordinal)
                    || c.Code.Contains(keyword, StringComparison.Ordinal))
                .Select(category => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["code"] = category.Code,
                    ["name"] = category.Name,
                    ["parentCode"] = category.ParentId.HasValue
                        && codeById.TryGetValue(category.ParentId.Value, out string? parentCode)
                            ? parentCode
                            : null,
                    ["levelNo"] = category.LevelNo,
                    ["unitGroupId"] = category.UnitGroupId,
                    ["status"] = (int)category.Status,
                }),
        ];
    }
}
