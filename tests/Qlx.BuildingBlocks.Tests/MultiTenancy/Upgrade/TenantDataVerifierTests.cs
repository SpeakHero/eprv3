/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 17:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 17:30:00 +08:00 中国标准时间 星期四
 * 描述：租户升级数据校验器纯函数测试（行哈希确定性 / 规范化 / 篡改检出 / 表级摘要）
 */

using Qlx.BuildingBlocks.MultiTenancy.Upgrade;

namespace Qlx.BuildingBlocks.Tests.MultiTenancy.Upgrade;

/// <summary>
/// <see cref="TenantDataVerifier"/> 的哈希 / 规范化纯函数测试：
/// 跨库同值必同串（规范化确定性）、任一列值变更必检出（篡改检出）。
/// </summary>
public sealed class TenantDataVerifierTests
{
    [Fact]
    public void RowHashIsDeterministicForSameValues()
    {
        object?[] values = [1L, "商品A", 9.99m, true, null];

        var first = TenantDataVerifier.ComputeRowHash(values);
        var second = TenantDataVerifier.ComputeRowHash(values);

        Assert.Equal(first, second);
    }

    [Fact]
    public void RowHashDiffersWhenAnyColumnChanges()
    {
        object?[] baseline = [1L, "商品A", 9.99m, true, null];
        var baselineHash = TenantDataVerifier.ComputeRowHash(baseline);

        object?[] tampered = [1L, "商品B", 9.99m, true, null];
        Assert.NotEqual(baselineHash, TenantDataVerifier.ComputeRowHash(tampered));
    }

    [Fact]
    public void NullAndDbNullNormalizeToSameToken()
    {
        var nullHash = TenantDataVerifier.ComputeRowHash([1L, null]);
        var dbNullHash = TenantDataVerifier.ComputeRowHash([1L, DBNull.Value]);

        Assert.Equal(nullHash, dbNullHash);
    }

    [Fact]
    public void DateTimeNormalizesToRoundTripFormat()
    {
        var value = new DateTime(2026, 10, 22, 8, 30, 15, DateTimeKind.Utc);

        var normalized = TenantDataVerifier.NormalizeValue(value);

        Assert.Contains("2026-10-22T08:30:15", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void BoolNormalizesToOneAndZero()
    {
        Assert.Equal("1", TenantDataVerifier.NormalizeValue(true));
        Assert.Equal("0", TenantDataVerifier.NormalizeValue(false));
    }

    [Fact]
    public void ByteArrayNormalizesToHex()
    {
        Assert.Equal("0102FF", TenantDataVerifier.NormalizeValue(new byte[] { 0x01, 0x02, 0xFF }));
    }

    [Fact]
    public void DecimalNormalizesWithInvariantCulture()
    {
        // 区域性小数分隔符（如德语逗号）不得影响规范化结果
        Assert.Equal("1234.56", TenantDataVerifier.NormalizeValue(1234.56m));
    }

    [Fact]
    public void TableDigestIsDeterministicForSameInputs()
    {
        string[] hashes = ["AAA", "BBB"];

        var first = TenantDataVerifier.ComputeTableDigest("[dbo].[bas_item]", 2, hashes);
        var second = TenantDataVerifier.ComputeTableDigest("[dbo].[bas_item]", 2, hashes);

        Assert.Equal(first, second);
    }

    [Fact]
    public void TableDigestDiffersWhenRowCountChanges()
    {
        string[] hashes = ["AAA"];

        var baseline = TenantDataVerifier.ComputeTableDigest("[dbo].[bas_item]", 1, hashes);
        var tampered = TenantDataVerifier.ComputeTableDigest("[dbo].[bas_item]", 2, hashes);

        Assert.NotEqual(baseline, tampered);
    }

    [Fact]
    public void TableDigestDiffersWhenSampleHashChanges()
    {
        var baseline = TenantDataVerifier.ComputeTableDigest("[dbo].[bas_item]", 1, ["AAA"]);
        var tampered = TenantDataVerifier.ComputeTableDigest("[dbo].[bas_item]", 1, ["AAB"]);

        Assert.NotEqual(baseline, tampered);
    }

    [Fact]
    public void VerificationExceptionCarriesTableName()
    {
        var exception = new TenantUpgradeVerificationException("[dbo].[bas_item]", "行数不一致");

        Assert.Equal("[dbo].[bas_item]", exception.TableName);
        Assert.Contains("行数不一致", exception.Message, StringComparison.Ordinal);
    }
}
