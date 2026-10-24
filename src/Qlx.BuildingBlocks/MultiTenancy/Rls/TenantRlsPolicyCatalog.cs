/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 01:15:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 01:15:00 +08:00 中国标准时间 星期三
 * 描述：RLS 策略目标表目录，从 EF 模型元数据自动推导需安装策略的租户表
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Qlx.Abstractions.Constants;

namespace Qlx.BuildingBlocks.MultiTenancy.Rls;

/// <summary>
/// RLS 策略目录：从 EF 模型元数据推导「含租户列的业务表」，
/// 保证安全策略表集合与模型表集合一致（架构测试亦据此断言）。
/// </summary>
public static class TenantRlsPolicyCatalog
{
    /// <summary>租户列属性名（实体侧，与全局模型约定一致）。</summary>
    public const string TenantIdPropertyName = QlxHostConstants.TenantEntityPropertyName;

    /// <summary>
    /// 构建目标表集合：仅包含已映射物理表且含 <c>TenantId</c> 属性的实体。
    /// </summary>
    /// <param name="model">EF 模型（<c>DbContext.Model</c>）。</param>
    /// <returns>按表名排序的目标表集合（去重）。</returns>
    public static IReadOnlyList<TenantRlsTable> Build(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var tables = new List<TenantRlsTable>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.GetTableName() is not { } tableName)
            {
                continue;
            }

            var tenantProperty = entityType.FindProperty(TenantIdPropertyName)
                ?? entityType.FindProperty("Tenant_Id");

            if (tenantProperty is null)
            {
                continue;
            }

            var schema = entityType.GetSchema() ?? "dbo";
            var tenantColumn = tenantProperty.GetColumnName() ?? "tenant_id";

            if (seen.Add($"{schema}.{tableName}"))
            {
                tables.Add(new TenantRlsTable(schema, tableName, tenantColumn));
            }
        }

        tables.Sort(static (left, right) => string.CompareOrdinal(left.TableName, right.TableName));
        return tables;
    }
}
