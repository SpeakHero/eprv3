/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：租户升级数据校验器：共享库 vs 独立库行数比对 + 均匀抽样逐行 SHA256 哈希比对
 */

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Qlx.BuildingBlocks.MultiTenancy.Upgrade;

/// <summary>
/// 校验汇总结果。
/// </summary>
/// <param name="TablesChecked">校验表数。</param>
/// <param name="TotalRowsCompared">行数比对累计行数。</param>
/// <param name="SamplesCompared">抽样比对累计行数。</param>
/// <param name="Digest">全量校验摘要（各表摘要再哈希，十六进制）。</param>
internal sealed record TenantVerifySummary(
    int TablesChecked,
    long TotalRowsCompared,
    int SamplesCompared,
    string Digest);

/// <summary>
/// 升级数据校验失败异常（含表名与明细，供失败任务落库排查）。
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="tableName">不一致表名。</param>
/// <param name="detail">不一致明细。</param>
internal sealed class TenantUpgradeVerificationException(string tableName, string detail) : Exception($"表 {tableName} 数据校验不一致：{detail}")
{


    /// <summary>不一致表名。</summary>
    public string TableName { get; } = tableName;
}

/// <summary>
/// 租户升级数据校验器（Verifying 阶段 + Cutover 前快速复核）。
/// 每表先双边 <c>COUNT_BIG</c> 比对；再按主键均匀步长抽样，双边取整行所有列值规范化后
/// 拼接做 SHA256 比对。任一不一致抛 <see cref="TenantUpgradeVerificationException"/>，
/// 由流水线标记任务失败并保留独立库现场。
/// </summary>
internal sealed partial class TenantDataVerifier(ILogger<TenantDataVerifier> logger)
{
    /// <summary>
    /// 执行校验。
    /// </summary>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="source">共享库连接信息。</param>
    /// <param name="target">目标独立库连接信息。</param>
    /// <param name="tables">表目录。</param>
    /// <param name="sampleSize">每表抽样行数（0 表示只做行数比对）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>校验汇总。</returns>
    internal async Task<TenantVerifySummary> VerifyAsync(
        long tenantId,
        TenantConnectionInfo source,
        TenantConnectionInfo target,
        IReadOnlyList<TenantTableDescriptor> tables,
        int sampleSize,
        CancellationToken cancellationToken)
    {
        await using var sourceConnection = new SqlConnection(source.ToConnectionString());
        await sourceConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var targetConnection = new SqlConnection(target.ToConnectionString());
        await targetConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

        long totalRows = 0;
        var totalSamples = 0;
        var tableDigests = new List<string>();

        foreach (var table in tables)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceCount = await CountRowsAsync(
                sourceConnection, table, tenantId, filterByTenant: true, cancellationToken).ConfigureAwait(false);
            var targetCount = await CountRowsAsync(
                targetConnection, table, tenantId, filterByTenant: false, cancellationToken).ConfigureAwait(false);

            if (sourceCount != targetCount)
            {
                throw new TenantUpgradeVerificationException(
                    table.QualifiedName,
                    $"行数不一致（共享库 {sourceCount} vs 独立库 {targetCount}）");
            }

            totalRows += sourceCount;

            var rowHashes = new List<string>();
            if (sampleSize > 0 && sourceCount > 0)
            {
                var sampleIds = await PickSampleKeysAsync(
                    sourceConnection, table, tenantId, sourceCount, sampleSize, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var id in sampleIds)
                {
                    var sourceHash = await ReadRowHashAsync(sourceConnection, table, id, tenantId, true, cancellationToken)
                        .ConfigureAwait(false);
                    var targetHash = await ReadRowHashAsync(targetConnection, table, id, tenantId, false, cancellationToken)
                        .ConfigureAwait(false);

                    if (!string.Equals(sourceHash, targetHash, StringComparison.Ordinal))
                    {
                        throw new TenantUpgradeVerificationException(
                            table.QualifiedName,
                            $"主键 {id} 行内容哈希不一致（共享库 {sourceHash ?? "缺失"} vs 独立库 {targetHash ?? "缺失"}）");
                    }

                    if (sourceHash is not null)
                    {
                        rowHashes.Add(sourceHash);
                    }
                }

                totalSamples += sampleIds.Count;
            }

            tableDigests.Add(ComputeTableDigest(table.QualifiedName, sourceCount, rowHashes));
            LogTableVerified(table.ModuleCode, table.TableName, sourceCount, rowHashes.Count);
        }

        return new TenantVerifySummary(
            tables.Count,
            totalRows,
            totalSamples,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', tableDigests)))));
    }

    /// <summary>
    /// 计算单行哈希：列值按 invariant 规则规范化（null→"∅"，byte[]→十六进制，
    /// DateTime/DateTimeOffset→"O" 格式）后竖线拼接，做 SHA256 返回十六进制。
    /// </summary>
    /// <param name="values">行所有列值（按列序）。</param>
    /// <returns>SHA256 十六进制摘要。</returns>
    internal static string ComputeRowHash(object?[] values)
    {
        var normalized = string.Join('|', values.Select(NormalizeValue));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    /// <summary>
    /// 计算表级摘要：表名 + 行数 + 全部抽样行哈希拼接后 SHA256。
    /// </summary>
    /// <param name="tableName">限定表名。</param>
    /// <param name="rowCount">行数。</param>
    /// <param name="rowHashes">抽样行哈希（按主键序）。</param>
    /// <returns>SHA256 十六进制摘要。</returns>
    internal static string ComputeTableDigest(string tableName, long rowCount, IReadOnlyList<string> rowHashes)
    {
        var payload = string.Join('|', [tableName, rowCount.ToString(CultureInfo.InvariantCulture), .. rowHashes]);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    /// <summary>列值规范化（确定性，跨库同值必同串）。</summary>
    internal static string NormalizeValue(object? value)
        => value switch
        {
            null or DBNull => "∅",
            byte[] bytes => Convert.ToHexString(bytes),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            bool flag => flag ? "1" : "0",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "∅",
        };

    private static async Task<long> CountRowsAsync(
        SqlConnection connection,
        TenantTableDescriptor table,
        long tenantId,
        bool filterByTenant,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = filterByTenant
            ? $"SELECT COUNT_BIG(*) FROM {table.QualifiedName} WHERE {TenantTableDescriptor.Quote(table.TenantColumn)} = @tenantId;"
            : $"SELECT COUNT_BIG(*) FROM {table.QualifiedName};";
        if (filterByTenant)
        {
            command.Parameters.Add(new SqlParameter("@tenantId", tenantId));
        }

        var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar is long value ? value : 0L;
    }

    /// <summary>按主键均匀步长抽取样本主键。</summary>
    private static async Task<IReadOnlyList<long>> PickSampleKeysAsync(
        SqlConnection connection,
        TenantTableDescriptor table,
        long tenantId,
        long rowCount,
        int sampleSize,
        CancellationToken cancellationToken)
    {
        var take = (int)Math.Min(sampleSize, rowCount);
        var step = Math.Max(1, rowCount / take);
        var quotedPk = TenantTableDescriptor.Quote(table.PrimaryKeyColumn);

        var keys = new List<long>(take);
        for (var i = 0; i < take; i++)
        {
            var offset = i * step;
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT {quotedPk} FROM {table.QualifiedName}
                WHERE {TenantTableDescriptor.Quote(table.TenantColumn)} = @tenantId
                ORDER BY {quotedPk}
                OFFSET @offset ROWS FETCH NEXT 1 ROWS ONLY;
                """;
            command.Parameters.Add(new SqlParameter("@tenantId", tenantId));
            command.Parameters.Add(new SqlParameter("@offset", offset));

            var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (scalar is long key)
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    /// <summary>读取单行全部列值并计算哈希；行缺失返回 null。</summary>
    private static async Task<string?> ReadRowHashAsync(
        SqlConnection connection,
        TenantTableDescriptor table,
        long key,
        long tenantId,
        bool filterByTenant,
        CancellationToken cancellationToken)
    {
        var quotedPk = TenantTableDescriptor.Quote(table.PrimaryKeyColumn);
        await using var command = connection.CreateCommand();
        command.CommandText = filterByTenant
            ? $"SELECT * FROM {table.QualifiedName} WHERE {quotedPk} = @key AND {TenantTableDescriptor.Quote(table.TenantColumn)} = @tenantId;"
            : $"SELECT * FROM {table.QualifiedName} WHERE {quotedPk} = @key;";
        command.Parameters.Add(new SqlParameter("@key", key));
        if (filterByTenant)
        {
            command.Parameters.Add(new SqlParameter("@tenantId", tenantId));
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var values = new object[reader.FieldCount];
        reader.GetValues(values);
        return ComputeRowHash(values);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "升级校验通过：模块 {ModuleCode} 表 {TableName}（行数 {RowCount}，抽样 {Samples}）")]
    private partial void LogTableVerified(string moduleCode, string tableName, long rowCount, int samples);
}
