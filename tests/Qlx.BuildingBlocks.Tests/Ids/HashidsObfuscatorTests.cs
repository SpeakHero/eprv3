/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 描述：Hashids 混淆器单元测试（可逆回环、盐隔离、非法输入、最小长度）
 */

using Qlx.BuildingBlocks.Ids;

namespace Qlx.BuildingBlocks.Tests.Ids;

public sealed class HashidsObfuscatorTests
{
    private static readonly long[] s_sampleIds = [0L, 1L, 42L, 1024L, 123_456_789L, 9_000_000_000_000_000_000L];

    [Fact]
    public void EncodeThenDecodeRoundtripsOriginalValues()
    {
        var obfuscator = new HashidsObfuscator("test-salt", minLength: 8);

        foreach (var id in s_sampleIds)
        {
            var encoded = obfuscator.Encode(id);
            Assert.False(string.IsNullOrWhiteSpace(encoded));
            Assert.True(obfuscator.TryDecode(encoded, out var decoded), $"编码值 {encoded} 解码失败");
            Assert.Equal(id, decoded);
        }
    }

    [Fact]
    public void DifferentSaltsProduceDifferentEncodings()
    {
        var first = new HashidsObfuscator("salt-a", minLength: 8);
        var second = new HashidsObfuscator("salt-b", minLength: 8);

        Assert.NotEqual(first.Encode(42L), second.Encode(42L));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!not-valid!!!")]
    public void TryDecodeRejectsInvalidInput(string? input)
    {
        var obfuscator = new HashidsObfuscator("test-salt", minLength: 8);

        Assert.False(obfuscator.TryDecode(input, out _));
    }

    [Fact]
    public void TryDecodeRejectsForeignSaltEncoding()
    {
        var foreign = new HashidsObfuscator("foreign-salt", minLength: 8);
        var local = new HashidsObfuscator("local-salt", minLength: 8);

        var foreignEncoded = foreign.Encode(42L);

        Assert.False(local.TryDecode(foreignEncoded, out _));
    }

    [Fact]
    public void MinLengthPadsEncodedOutput()
    {
        var obfuscator = new HashidsObfuscator("test-salt", minLength: 16);

        Assert.True(obfuscator.Encode(1L).Length >= 16);
    }

    [Fact]
    public void EmptySaltThrows()
    {
        Assert.Throws<ArgumentException>(() => new HashidsObfuscator(string.Empty));
    }
}
