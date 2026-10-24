/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：租户表描述与模型目录：从 EF 模型收集含 tenant_id 列且具备单 long 主键的业务表，供复制/校验/清理共用
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Qlx.Abstractions.Constants;

namespace Qlx.BuildingBlocks.MultiTenancy.Upgrade;

/// <summary>
/// 租户业务表描述（复制 / 校验 / 清理共用的最小元数据）。
/// </summary>
/// <param name="ModuleCode">所属模块编码。</param>
/// <param name="Schema">表架构（可能为 null，SQL Server 默认 dbo）。</param>
/// <param name="TableName">表名。</param>
/// <param name="PrimaryKeyColumn">主键列名（单列 long）。</param>
/// <param name="TenantColumn">租户列名。</param>
/// <param name="UpdatedAtColumn">更新时间列名（无审计时间列的表为 null）。</param>
internal sealed record TenantTableDescriptor(
    string ModuleCode,
    string? Schema,
    string TableName,
    string PrimaryKeyColumn,
    string TenantColumn,
    string? UpdatedAtColumn)
{
    /// <summary>限定表名（[schema].[table]，schema 缺失时按 dbo）。</summary>
    public string QualifiedName
        => $"{Quote(Schema ?? "dbo")}.{Quote(TableName)}";

    /// <summary>断点 / 字典键（模块编码 + 表名，大小写不敏感场景由调用方规范化）。</summary>
    public string Key => $"{ModuleCode}|{TableName}";

    /// <summary>T-SQL 标识符转义。</summary>
    public static string Quote(string identifier)
        => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
}

/// <summary>
/// 从模块 EF 模型收集租户业务表目录。仅纳入：有物理表、含 tenant 列、且主键为单列 long 的表
/// （无主键 / 联合主键 / 无租户列的表跳过并由调用方记日志）。
/// </summary>
internal static class TenantTableCatalog
{
    /// <summary>
    /// 从模块模型收集表描述。
    /// </summary>
    /// <param name="model">模块 DbContext 模型。</param>
    /// <param name="moduleCode">模块编码。</param>
    /// <param name="skipped">被跳过的表名输出列表（无租户列或无单 long 主键）。</param>
    /// <returns>表描述列表（按表名去重）。</returns>
    public static IReadOnlyList<TenantTableDescriptor> Collect(
        IModel model, string moduleCode, ICollection<string>? skipped = null)
    {
        var result = new List<TenantTableDescriptor>();

        foreach (var entityType in model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (string.IsNullOrWhiteSpace(tableName))
            {
                continue;
            }

            var tenantProperty = entityType.FindProperty(QlxHostConstants.TenantEntityPropertyName);
            var primaryKey = entityType.FindPrimaryKey();

            if (tenantProperty is null || primaryKey is null || primaryKey.Properties.Count != 1)
            {
                skipped?.Add($"{moduleCode}:{tableName}");
                continue;
            }

            var keyProperty = primaryKey.Properties[0];
            if (keyProperty.ClrType != typeof(long))
            {
                skipped?.Add($"{moduleCode}:{tableName}");
                continue;
            }

            var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
            var tenantColumn = tenantProperty.GetColumnName(storeObject);
            var keyColumn = keyProperty.GetColumnName(storeObject);
            if (string.IsNullOrWhiteSpace(tenantColumn) || string.IsNullOrWhiteSpace(keyColumn))
            {
                skipped?.Add($"{moduleCode}:{tableName}");
                continue;
            }

            var updatedAtColumn = entityType.FindProperty("UpdatedAt")?.GetColumnName(storeObject);

            result.Add(new TenantTableDescriptor(
                moduleCode,
                entityType.GetSchema(),
                tableName,
                keyColumn,
                tenantColumn,
                updatedAtColumn));
        }

        return [.. result.DistinctBy(static t => t.QualifiedName, StringComparer.OrdinalIgnoreCase)];
    }
}

/// <summary>
/// 复制 / 校验 / 清理用快照解析器：直接返回已解析连接信息，不触发门户库查询与缓存。
/// </summary>
internal sealed class SnapshotTenantDatabaseResolver(
    long tenantId,
    TenantConnectionInfo connectionInfo) : ITenantDatabaseResolver
{
    /// <inheritdoc />
    public TenantConnectionInfo Resolve(long requestedTenantId)
    {
        if (requestedTenantId != tenantId)
        {
            throw new InvalidOperationException(
                $"升级流水线快照上下文租户不匹配：期望 {tenantId}，实际 {requestedTenantId}");
        }

        return connectionInfo;
    }

    /// <inheritdoc />
    public Task PreloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public void Invalidate(long requestedTenantId)
    {
    }
}
