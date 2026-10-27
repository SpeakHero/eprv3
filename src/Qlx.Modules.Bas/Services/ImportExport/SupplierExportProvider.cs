/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 01:50:00 +08:00 中国标准时间 星期一
 * 描述：供应商导出数据源——单次批量查询 + 扁平原始字段投影（含编码，枚举以数值下发）
 */

using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.ImportExport;
using Qlx.Modules.Bas.Repositories;

namespace Qlx.Modules.Bas.Services.ImportExport;

/// <summary>
/// 供应商导出数据源（模板 <c>bas.supplier.export</c>）。一次查询取全量匹配行，
/// 字段保持扁平原始值（枚举以数值下发、金额不格式化，B-2）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文（交叉校验导出租户）。</param>
/// <param name="repo">Bas 租户仓储。</param>
internal sealed class SupplierExportProvider(
    ITenantContext tenantCtx,
    IBasTenantRepository repo) : IExportDataProvider
{
    /// <inheritdoc />
    public string TemplateCode => BasImportTemplateDefinitionProvider.SupplierExport;

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(
        IReadOnlyDictionary<string, object?> criteria,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        long currentTenantId = tenantCtx.TenantId
            ?? throw new ForbiddenException("未识别到租户上下文");
        long restoredTenantId = ImportRowValues.ResolveTenantId(tenantId, currentTenantId);

        IReadOnlyList<Domain.Supplier.Supplier> suppliers = await repo.ListSuppliersForExportAsync(
            ImportRowValues.CriteriaInt32(criteria, "status"),
            ImportRowValues.CriteriaString(criteria, "keyword"),
            cancellationToken).ConfigureAwait(false);

        // 租户还原后核验查询结果归属，防止仓储过滤口径变化导致跨租户数据外泄
        return
        [
            .. suppliers
                .Where(s => s.TenantId == restoredTenantId)
                .Select(static supplier => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["code"] = supplier.Code,
                    ["name"] = supplier.Name,
                    ["uscc"] = supplier.Uscc,
                    ["supplierType"] = (int)supplier.SupplierType,
                    ["contact"] = supplier.Contact,
                    ["phone"] = supplier.Phone,
                    ["grade"] = (int)supplier.Grade,
                    ["status"] = (int)supplier.Status,
                }),
        ];
    }
}
