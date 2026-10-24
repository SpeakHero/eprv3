/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 20:15:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 20:15:00 +08:00 中国标准时间 星期五
 * 描述：通用校验正则公共层单元测试——格式正反用例 + 与改造前旧模式（Bas 模块内联正则）的等价性回归
 */

using System.Text.RegularExpressions;
using Qlx.BuildingBlocks.Validation;

namespace Qlx.BuildingBlocks.Tests.Validation;

public sealed class CommonValidationPatternsTests
{
    // 改造前 Bas 模块 TenantAdminService 的内联模式（无任何 RegexOptions），用于等价性回归对比
    private static readonly Regex s_legacyPhone = new(@"^1\d{10}$");
    private static readonly Regex s_legacyEmail = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$");

    private static readonly string[] s_accountCases =
    [
        "13800138000", "10000000000", "19912345678",  // 合法手机（宽松：1 开头 11 位）
        "23800138000", "1380013800", "138001380000", "1380013800a", " 13800138000", "13800138000 ", // 非法手机
        "a@b.com", "speakhero@qq.com", "x.y+z@sub.domain.cn",       // 合法邮箱
        "a@b", "a b@c.com", "a@b .com", "@b.com", "a@.com", "a@b.c om", // 非法邮箱
        "", "   ", "1", "1380013800@x.com",
    ];

    [Fact]
    public void CnMobilePhoneMatchesLegacyPatternExactly()
    {
        foreach (var input in s_accountCases)
        {
            Assert.Equal(s_legacyPhone.IsMatch(input), CommonValidationPatterns.CnMobilePhone.IsMatch(input));
        }
    }

    [Fact]
    public void EmailMatchesLegacyPatternExactly()
    {
        foreach (var input in s_accountCases)
        {
            Assert.Equal(s_legacyEmail.IsMatch(input), CommonValidationPatterns.Email.IsMatch(input));
        }
    }

    [Theory]
    [InlineData("13800138000", true)]
    [InlineData("10000000000", true)]
    [InlineData("23800138000", false)]
    [InlineData("1380013800", false)]
    [InlineData("138001380000", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    public void IsCnMobilePhone(string? input, bool expected)
        => Assert.Equal(expected, CommonValidationPatterns.IsCnMobilePhone(input));

    [Theory]
    [InlineData("a@b.com", true)]
    [InlineData("speakhero@qq.com", true)]
    [InlineData("a@b", false)]
    [InlineData("a b@c.com", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsEmail(string? input, bool expected)
        => Assert.Equal(expected, CommonValidationPatterns.IsEmail(input));

    [Theory]
    [InlineData("11010119900307777X", true)]
    [InlineData("11010119900307777x", true)]
    [InlineData("110101199003077778", true)]
    [InlineData("123456789012345", true)]      // 15 位
    [InlineData("12345678901234", false)]      // 14 位
    [InlineData("1234567890123456", false)]    // 16 位
    [InlineData("11010119900307777A", false)]  // 末位仅允许 X/x
    [InlineData(null, false)]
    public void IsCnIdCard(string? input, bool expected)
        => Assert.Equal(expected, CommonValidationPatterns.IsCnIdCard(input));

    [Theory]
    [InlineData("192.168.1.1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("255.255.255.255", true)]
    [InlineData("256.0.0.1", false)]
    [InlineData("192.168.1", false)]
    [InlineData("192.168.1.1.1", false)]
    [InlineData("01.2.3.4", false)]            // 前导零段（01）不允许
    [InlineData(null, false)]
    public void IsIPv4(string? input, bool expected)
        => Assert.Equal(expected, CommonValidationPatterns.IsIPv4(input));

    [Theory]
    [InlineData("https://www.example.com", true)]
    [InlineData("http://example.com:8080/path/to?q=1", true)]
    [InlineData("https://sub.domain.cn/", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("https://", false)]
    [InlineData("https://exa mple.com", false)]
    [InlineData(null, false)]
    public void IsUrl(string? input, bool expected)
        => Assert.Equal(expected, CommonValidationPatterns.IsUrl(input));

    [Fact]
    public void AllPatternsArePrecompiledWithTimeout()
    {
        // 统一标准：CultureInvariant + 有限匹配超时（防 ReDoS）
        foreach (var pattern in new[]
        {
            CommonValidationPatterns.CnMobilePhone, CommonValidationPatterns.Email,
            CommonValidationPatterns.CnIdCard, CommonValidationPatterns.IPv4, CommonValidationPatterns.Url,
        })
        {
            Assert.True(pattern.Options.HasFlag(RegexOptions.CultureInvariant), $"{pattern} 缺少 CultureInvariant");
            Assert.NotEqual(Regex.InfiniteMatchTimeout, pattern.MatchTimeout);
        }
    }

    [Fact]
    public void PatternPropertiesReturnCachedSingleton()
        => Assert.Same(CommonValidationPatterns.CnMobilePhone, CommonValidationPatterns.CnMobilePhone);
}
