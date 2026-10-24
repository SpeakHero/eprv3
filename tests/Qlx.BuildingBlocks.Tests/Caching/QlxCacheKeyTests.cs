/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 00:55:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 00:55:00 +08:00 中国标准时间 星期三
 * 描述：租户缓存键规范测试：格式固定、租户段隔离、非法输入拒绝
 */

using Qlx.BuildingBlocks.Caching;

namespace Qlx.BuildingBlocks.Tests.Caching;

/// <summary>
/// <see cref="QlxCacheKey.BuildTenantKey"/> 规范测试（B3-02）。
/// </summary>
public sealed class QlxCacheKeyTests
{
    [Fact]
    public void BuildTenantKeyUsesFixedFormat()
    {
        var key = QlxCacheKey.BuildTenantKey(123456789, "bas", "user:42");

        Assert.Equal("tenant:123456789:bas:user:42", key);
    }

    [Fact]
    public void BuildTenantKeySeparatesTenants()
    {
        var a = QlxCacheKey.BuildTenantKey(1, "bas", "profile");
        var b = QlxCacheKey.BuildTenantKey(2, "bas", "profile");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void BuildTenantKeyRejectsNonPositiveTenantId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => QlxCacheKey.BuildTenantKey(0, "bas", "profile"));
        Assert.Throws<ArgumentOutOfRangeException>(() => QlxCacheKey.BuildTenantKey(-1, "bas", "profile"));
    }

    [Fact]
    public void BuildTenantKeyRejectsModuleWithSeparator()
    {
        Assert.Throws<ArgumentException>(() => QlxCacheKey.BuildTenantKey(1, "bas:extra", "profile"));
        Assert.Throws<ArgumentException>(() => QlxCacheKey.BuildTenantKey(1, " ", "profile"));
    }

    [Fact]
    public void BuildTenantKeyRejectsEmptyBusinessKey()
    {
        Assert.Throws<ArgumentException>(() => QlxCacheKey.BuildTenantKey(1, "bas", ""));
    }
}
