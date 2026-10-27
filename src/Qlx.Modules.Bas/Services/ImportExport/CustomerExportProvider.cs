/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 01:50:00 +08:00 中国标准时间 星期一
 * 描述：客户导出数据源——单次批量查询 + 扁平原始字段投影（含编码，额度为原始 decimal）
 */

using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.ImportExport;
using Qlx.Modules.Bas.Repositories;

namespace Qlx.Modules.Bas.Services.ImportExport;

/// <summary>
/// 客户导出数据源（模板 <c>bas.customer.export</c>）。一次查询取全量匹配行，
/// 信用额度以原始 decimal 下发（不做金额显示格式化，B-2-2）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文（交叉校验导出租户）。</param>
/// <param name="repo">Bas 租户仓储。</param>
internal sealed class CustomerExportProvider(
    ITenantContext tenantCtx,
    IBasTenantRepository repo) : IExportDataProvider
{
    /// <inheritdoc />
    public string TemplateCode => BasImportTemplateDefinitionProvider.CustomerExport;

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(
        IReadOnlyDictionary<string, object?> criteria,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        long currentTenantId = tenantCtx.TenantId
            ?? throw new ForbiddenException("未识别到租户上下文");
        long restoredTenantId = ImportRowValues.ResolveTenantId(tenantId, currentTenantId);

        IReadOnlyList<Domain.Customer.Customer> customers = await repo.ListCustomersForExportAsync(
            ImportRowValues.CriteriaInt32(criteria, "status"),
            ImportRowValues.CriteriaString(criteria, "keyword"),
            cancellationToken).ConfigureAwait(false);

        return
        [
            .. customers
                .Where(c => c.TenantId == restoredTenantId)
                .Select(static customer => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["code"] = customer.Code,
                    ["name"] = customer.Name,
                    ["uscc"] = customer.Uscc,
                    ["customerType"] = customer.CustomerType,
                    ["contact"] = customer.Contact,
                    ["phone"] = customer.Phone,
                    ["creditGrade"] = (int)customer.CreditGrade,
                    ["creditLimit"] = customer.CreditLimit,
                    ["creditUsed"] = customer.CreditUsed,
                    ["paymentTerms"] = customer.PaymentTerms,
                    ["status"] = (int)customer.Status,
                }),
        ];
    }
}
