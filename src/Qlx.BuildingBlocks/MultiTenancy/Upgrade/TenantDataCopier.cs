/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：租户数据复制器：共享库 → 独立库按键集分页批量复制（幂等断点续跑）与增量补差
 */

using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Tenancy;

namespace Qlx.BuildingBlocks.MultiTenancy.Upgrade;

/// <summary>
/// 租户数据复制器（升级流水线 Copying / Cutover 阶段使用）。
/// <para>
/// 全量复制：逐模块逐表按键集分页（WHERE tenant_id AND id &gt; lastKey ORDER BY id）读取共享库，
/// 每批先在目标库按主键范围幂等删除再 <see cref="SqlBulkCopy"/> 写入，批末落断点——
/// 任务失败重试时从断点继续，已复制批次不产生重复行。
/// </para>
/// <para>
/// 增量补差（Cutover 前）：含 updated_at 列的表按 <c>updated_at &gt; since</c> 重新 upsert；
/// 无 updated_at 列的表按 <c>id &gt; 断点</c> 追加。
/// </para>
/// 仅复制模型目录纳入的表（含租户列且单列 long 主键），其余表跳过并记日志。
/// </summary>
/// <remarks>构造函数。</remarks>
internal sealed partial class TenantDataCopier(
    IServiceProvider serviceProvider,
    IOptions<TenantUpgradeOptions> options,
    ILogger<TenantDataCopier> logger)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IOptions<TenantUpgradeOptions> _options = options;
    private readonly ILogger<TenantDataCopier> _logger = logger;


    /// <summary>
    /// 收集全部模块的租户业务表目录（模型元数据，供复制 / 校验 / 清理共用）。
    /// </summary>
    /// <param name="modules">模块迁移计划（DbContext 类型 + 模块编码）。</param>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="source">共享库连接信息（仅用于构建模型上下文）。</param>
    internal IReadOnlyList<TenantTableDescriptor> CollectModuleTables(
        IReadOnlyList<(Type DbContextType, string ModuleCode)> modules,
        long tenantId,
        TenantConnectionInfo source)
    {
        var tables = new List<TenantTableDescriptor>();
        foreach (var (dbContextType, moduleCode) in modules)
        {
            var skipped = new List<string>();
            using (var context = CreateModelContext(dbContextType, tenantId, source))
            {
                tables.AddRange(TenantTableCatalog.Collect(context.Model, moduleCode, skipped));
            }

            foreach (var table in skipped)
            {
                LogTableSkipped(table);
            }
        }

        return [.. tables.DistinctBy(static t => t.QualifiedName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// 全量复制：逐表按键集分页复制共享库中指定租户的数据到独立库。
    /// </summary>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="source">共享库连接信息。</param>
    /// <param name="target">目标独立库连接信息。</param>
    /// <param name="tables">表目录（<see cref="CollectModuleTables"/>）。</param>
    /// <param name="checkpoints">既有断点（重试续跑；键为 <see cref="TenantTableDescriptor.Key"/>）。</param>
    /// <param name="checkpointSaver">批末断点保存回调（模块编码、表名、最大主键、表累计行数）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>累计复制行数。</returns>
    internal async Task<long> CopyTenantDataAsync(
        long tenantId,
        TenantConnectionInfo source,
        TenantConnectionInfo target,
        IReadOnlyList<TenantTableDescriptor> tables,
        IReadOnlyDictionary<string, TenantUpgradeCheckpointSnapshot> checkpoints,
        Func<string, string, long, long, CancellationToken, Task> checkpointSaver,
        CancellationToken cancellationToken)
    {
        var batchSize = _options.Value.BatchSize;
        long totalRows = checkpoints.Values.Sum(static c => c.RowsCopied);

        await using var sourceConnection = new SqlConnection(source.ToConnectionString());
        await sourceConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var targetConnection = new SqlConnection(target.ToConnectionString());
        await targetConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (var table in tables)
        {
            cancellationToken.ThrowIfCancellationRequested();

            checkpoints.TryGetValue(table.Key, out var checkpoint);
            var lastKey = checkpoint?.LastKey ?? 0L;
            var tableRows = checkpoint?.RowsCopied ?? 0L;

            while (true)
            {
                using var batch = await ReadBatchAsync(
                    sourceConnection, table, tenantId, lastKey, batchSize,
                    updatedAfter: null, cancellationToken).ConfigureAwait(false);

                if (batch.RowCount == 0)
                {
                    break;
                }

                await UpsertBatchAsync(targetConnection, table, batch, batchSize, cancellationToken)
                    .ConfigureAwait(false);

                lastKey = batch.MaxKey;
                tableRows += batch.RowCount;
                totalRows += batch.RowCount;

                await checkpointSaver(table.ModuleCode, table.TableName, lastKey, tableRows, cancellationToken)
                    .ConfigureAwait(false);
            }

            LogTableCopied(table.ModuleCode, table.TableName, tableRows);
        }

        return totalRows;
    }

    /// <summary>
    /// 增量补差（Cutover 前最后同步）：含 updated_at 列的表按时间戳 upsert 变更行，
    /// 无 updated_at 列的表按断点主键追加新行。
    /// </summary>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="source">共享库连接信息。</param>
    /// <param name="target">目标独立库连接信息。</param>
    /// <param name="tables">表目录。</param>
    /// <param name="checkpoints">全量复制结束时的断点。</param>
    /// <param name="since">变更时间下界（任务创建时间，覆盖复制期间的写入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    internal async Task CatchUpAsync(
        long tenantId,
        TenantConnectionInfo source,
        TenantConnectionInfo target,
        IReadOnlyList<TenantTableDescriptor> tables,
        IReadOnlyDictionary<string, TenantUpgradeCheckpointSnapshot> checkpoints,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        var batchSize = _options.Value.BatchSize;

        await using var sourceConnection = new SqlConnection(source.ToConnectionString());
        await sourceConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var targetConnection = new SqlConnection(target.ToConnectionString());
        await targetConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (var table in tables)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DateTimeOffset? updatedAfter = table.UpdatedAtColumn is not null ? since : null;
            if (updatedAfter is null && !checkpoints.ContainsKey(table.Key))
            {
                // 无时间列且全量阶段未复制过该表（空表无断点）：无需补差
                continue;
            }

            var lastKey = 0L;
            long tableRows = 0;
            while (true)
            {
                var baseKey = updatedAfter is null && checkpoints.TryGetValue(table.Key, out var cp)
                    ? cp.LastKey
                    : 0L;
                using var batch = await ReadBatchAsync(
                    sourceConnection, table, tenantId, Math.Max(lastKey, baseKey), batchSize,
                    updatedAfter, cancellationToken).ConfigureAwait(false);

                if (batch.RowCount == 0)
                {
                    break;
                }

                await UpsertBatchAsync(targetConnection, table, batch, batchSize, cancellationToken)
                    .ConfigureAwait(false);

                lastKey = batch.MaxKey;
                tableRows += batch.RowCount;
            }

            if (tableRows > 0)
            {
                LogCatchUpTable(table.ModuleCode, table.TableName, tableRows);
            }
        }
    }

    /// <summary>按键集分页读取一批源数据（可选按更新时间过滤补差）。</summary>
    private static async Task<BatchData> ReadBatchAsync(
        SqlConnection sourceConnection,
        TenantTableDescriptor table,
        long tenantId,
        long lastKey,
        int batchSize,
        DateTimeOffset? updatedAfter,
        CancellationToken cancellationToken)
    {
        var quotedTable = table.QualifiedName;
        var quotedPk = TenantTableDescriptor.Quote(table.PrimaryKeyColumn);
        var quotedTenant = TenantTableDescriptor.Quote(table.TenantColumn);

        var whereClause = updatedAfter is null
            ? $"{quotedTenant} = @tenantId AND {quotedPk} > @lastKey"
            : $"{quotedTenant} = @tenantId AND {quotedPk} > @lastKey AND {TenantTableDescriptor.Quote(table.UpdatedAtColumn!)} > @updatedAfter";

        await using var command = sourceConnection.CreateCommand();
        command.CommandText = $"""
            SELECT * FROM {quotedTable}
            WHERE {whereClause}
            ORDER BY {quotedPk}
            OFFSET 0 ROWS FETCH NEXT @batchSize ROWS ONLY;
            """;
        command.Parameters.Add(new SqlParameter("@tenantId", tenantId));
        command.Parameters.Add(new SqlParameter("@lastKey", lastKey));
        command.Parameters.Add(new SqlParameter("@batchSize", batchSize));
        if (updatedAfter is not null)
        {
            command.Parameters.Add(new SqlParameter("@updatedAfter", updatedAfter.Value));
        }

        // DataTable 所有权随 BatchData 转移至调用方（不得 using 声明）
        var dataTable = new DataTable();
        long minKey = long.MaxValue;
        long maxKey = 0L;
        var rowCount = 0;

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            var pkOrdinal = -1;
            for (var i = 0; i < reader.FieldCount; i++)
            {
                dataTable.Columns.Add(reader.GetName(i), reader.GetFieldType(i));
                if (string.Equals(reader.GetName(i), table.PrimaryKeyColumn, StringComparison.OrdinalIgnoreCase))
                {
                    pkOrdinal = i;
                }
            }

            if (pkOrdinal < 0)
            {
                throw new InvalidOperationException(
                    $"表 {quotedTable} 读取结果缺少主键列 {table.PrimaryKeyColumn}，无法继续复制");
            }

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                dataTable.Rows.Add(values);

                var key = Convert.ToInt64(values[pkOrdinal], System.Globalization.CultureInfo.InvariantCulture);
                minKey = Math.Min(minKey, key);
                maxKey = Math.Max(maxKey, key);
                rowCount++;
            }
        }

        // DataTable 所有权随 BatchData 转移（调用方负责释放）
        return rowCount == 0
            ? BatchData.Empty(dataTable)
            : BatchData.Of(dataTable, rowCount, minKey, maxKey);
    }

    /// <summary>目标库幂等写入：先按主键范围删除本批既有行，再批量插入。</summary>
    private static async Task UpsertBatchAsync(
        SqlConnection targetConnection,
        TenantTableDescriptor table,
        BatchData batch,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var quotedPk = TenantTableDescriptor.Quote(table.PrimaryKeyColumn);

        await using (var deleteCommand = targetConnection.CreateCommand())
        {
            deleteCommand.CommandText =
                $"DELETE FROM {table.QualifiedName} WHERE {quotedPk} >= @minKey AND {quotedPk} <= @maxKey;";
            deleteCommand.Parameters.Add(new SqlParameter("@minKey", batch.MinKey));
            deleteCommand.Parameters.Add(new SqlParameter("@maxKey", batch.MaxKey));
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        using var bulkCopy = new SqlBulkCopy(targetConnection)
        {
            DestinationTableName = table.QualifiedName,
            BatchSize = batchSize,
        };
        foreach (DataColumn column in batch.Data.Columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulkCopy.WriteToServerAsync(batch.Data, cancellationToken).ConfigureAwait(false);
    }

    private DbContext CreateModelContext(
        Type dbContextType, long tenantId, TenantConnectionInfo connectionInfo)
    {
        var optionsBuilder = new DbContextOptionsBuilder();
        optionsBuilder.UseSqlServer(connectionInfo.ToConnectionString());

        var snapshotResolver = new SnapshotTenantDatabaseResolver(tenantId, connectionInfo);
        var ctorArgs = new object[] { snapshotResolver, tenantId, optionsBuilder.Options };
        return (DbContext)ActivatorUtilities.CreateInstance(_serviceProvider, dbContextType, ctorArgs);
    }

    /// <summary>单批数据（含主键范围，供幂等删除）。</summary>
    private sealed class BatchData : IDisposable
    {
        private BatchData(DataTable data, int rowCount, long minKey, long maxKey)
        {
            Data = data;
            RowCount = rowCount;
            MinKey = minKey;
            MaxKey = maxKey;
        }

        /// <summary>非空批次。</summary>
        public static BatchData Of(DataTable data, int rowCount, long minKey, long maxKey)
            => new(data, rowCount, minKey, maxKey);

        /// <summary>空批次（DataTable 仍需释放）。</summary>
        public static BatchData Empty(DataTable data) => new(data, 0, 0, 0);

        /// <summary>本批数据表。</summary>
        public DataTable Data { get; }

        /// <summary>本批行数。</summary>
        public int RowCount { get; }

        /// <summary>本批最小主键。</summary>
        public long MinKey { get; }

        /// <summary>本批最大主键。</summary>
        public long MaxKey { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            Data.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "升级复制跳过表 {Table}（无租户列 / 无单列 long 主键）")]
    private partial void LogTableSkipped(string table);

    [LoggerMessage(Level = LogLevel.Information, Message = "升级复制完成：模块 {ModuleCode} 表 {TableName} 累计 {RowsCopied} 行")]
    private partial void LogTableCopied(string moduleCode, string tableName, long rowsCopied);

    [LoggerMessage(Level = LogLevel.Information, Message = "升级增量补差：模块 {ModuleCode} 表 {TableName} 补 {RowsCopied} 行")]
    private partial void LogCatchUpTable(string moduleCode, string tableName, long rowsCopied);
}
