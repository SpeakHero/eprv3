/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 03:40:00 +08:00 中国标准时间 星期五
 * 描述：实体级表名前缀特性单元测试（B-0：合法前缀保留 / 非法前缀拒绝）
 */

using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.BuildingBlocks.Tests.MultiTenancy;

public sealed class TenantTablePrefixAttributeTests
{
    [Theory]
    [InlineData("fil_")]
    [InlineData("job_")]
    [InlineData("msg_")]
    // 空串合法：显式声明不叠加前缀（表名 = DbSet 名 snake_case，自带前缀语义，如 job_definitions）
    [InlineData("")]
    public void ValidPrefixIsPreserved(string prefix)
    {
        var attribute = new TenantTablePrefixAttribute(prefix);
        Assert.Equal(prefix, attribute.Prefix);
    }

    [Theory]
    [InlineData("fil")]
    [InlineData("  ")]
    public void InvalidPrefixThrowsArgumentException(string prefix)
        => Assert.Throws<ArgumentException>(() => new TenantTablePrefixAttribute(prefix));

    [Fact]
    public void NullPrefixThrowsArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => new TenantTablePrefixAttribute(null!));

    [Fact]
    public void PrefixWithSpaceThrows()
        => Assert.Throws<ArgumentException>(() => new TenantTablePrefixAttribute("fil _"));

    [Fact]
    public void PrefixWithDotThrows()
        => Assert.Throws<ArgumentException>(() => new TenantTablePrefixAttribute("fil._"));

    [Fact]
    public void AttributeIsNotInheritedAndSingleUse()
    {
        var usage = Attribute.GetCustomAttribute(
            typeof(TenantTablePrefixAttribute), typeof(AttributeUsageAttribute)) as AttributeUsageAttribute;

        Assert.NotNull(usage);
        Assert.False(usage.Inherited);
        Assert.False(usage.AllowMultiple);
        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
    }
}
