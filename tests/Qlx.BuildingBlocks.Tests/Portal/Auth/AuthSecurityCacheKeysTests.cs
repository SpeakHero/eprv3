/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25
 * 描述：AuthSecurityCacheKeys 白盒测试——哈希归一化、键结构合规（C-1-2）、无明文泄露、类别隔离、注入防护
 */

using System.Text.RegularExpressions;
using Qlx.Modules.Portal.Auth;

namespace Qlx.BuildingBlocks.Tests.Portal.Auth;

public sealed class AuthSecurityCacheKeysTests
{
    private static readonly Regex HashSegmentPattern = new("^[0-9a-f]{32}$", RegexOptions.Compiled);

    [Fact]
    public void PasswordFailKeyFollowsPortalKeyConvention()
    {
        var key = AuthSecurityCacheKeys.PasswordFail("admin");

        // QlxCacheKey.BuildPortalKey → _portal:auth:pwfail:{hash}
        Assert.StartsWith("_portal:auth:pwfail:", key, StringComparison.Ordinal);
        Assert.Matches(HashSegmentPattern, key["_portal:auth:pwfail:".Length..]);
    }

    [Fact]
    public void AllCategoryKeysAreDistinctForSameInput()
    {
        const string input = "13800000001";

        var keys = new[]
        {
            AuthSecurityCacheKeys.PasswordFail(input),
            AuthSecurityCacheKeys.Lockout(input),
            AuthSecurityCacheKeys.IpFail(input),
            AuthSecurityCacheKeys.IpBlock(input),
        };

        Assert.Equal(4, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(keys, k => k.StartsWith("_portal:auth:pwfail:", StringComparison.Ordinal));
        Assert.Contains(keys, k => k.StartsWith("_portal:auth:lockout:", StringComparison.Ordinal));
        Assert.Contains(keys, k => k.StartsWith("_portal:auth:ipfail:", StringComparison.Ordinal));
        Assert.Contains(keys, k => k.StartsWith("_portal:auth:ipblock:", StringComparison.Ordinal));
    }

    [Fact]
    public void KeysNeverContainRawInput()
    {
        const string account = "admin'-- OR 1=1";

        Assert.DoesNotContain(account, AuthSecurityCacheKeys.PasswordFail(account), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("admin", AuthSecurityCacheKeys.Lockout(account), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("192.168.1.1", AuthSecurityCacheKeys.IpFail("192.168.1.1"), StringComparison.Ordinal);
        Assert.DoesNotContain("192.168.1.1", AuthSecurityCacheKeys.IpBlock("192.168.1.1"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Admin", "admin")]           // 大小写归一化
    [InlineData(" admin ", "admin")]          // 前后空白 Trim
    [InlineData("  ADMIN  ", "admin")]        // 组合
    public void HashNormalizesCaseAndWhitespace(string left, string right)
    {
        Assert.Equal(AuthSecurityCacheKeys.PasswordFail(left), AuthSecurityCacheKeys.PasswordFail(right));
        Assert.Equal(AuthSecurityCacheKeys.IpFail(left), AuthSecurityCacheKeys.IpFail(right));
    }

    [Fact]
    public void DifferentInputsProduceDifferentKeys()
    {
        Assert.NotEqual(
            AuthSecurityCacheKeys.PasswordFail("user-a"),
            AuthSecurityCacheKeys.PasswordFail("user-b"));
        Assert.NotEqual(
            AuthSecurityCacheKeys.IpBlock("10.0.0.1"),
            AuthSecurityCacheKeys.IpBlock("10.0.0.2"));
    }

    [Fact]
    public void EmptyAndWhitespaceInputStillProduceStableHashKey()
    {
        var empty = AuthSecurityCacheKeys.Lockout(string.Empty);
        var whitespace = AuthSecurityCacheKeys.Lockout("   ");

        // Trim 后均为空串 → 同一哈希；键结构仍合规（防注入目标是不抛异常且键形态固定）
        Assert.Equal(empty, whitespace);
        Assert.Matches(HashSegmentPattern, empty["_portal:auth:lockout:".Length..]);
    }
}
