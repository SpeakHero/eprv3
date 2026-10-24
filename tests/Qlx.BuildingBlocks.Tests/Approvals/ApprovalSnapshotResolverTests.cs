/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 11:20:00 +08:00 中国标准时间 星期五
 * 描述：审批快照变量解析器测试——类型映射（string/long/double/bool/null）、大小写不敏感、
 *       缺失字段返回 false（严格模式引擎抛未解析）、非对象快照拒绝
 */

using Qlx.Modules.Support.Services.Approvals;
using Xunit;

namespace Qlx.BuildingBlocks.Tests.Approvals;

/// <summary>
/// <see cref="ApprovalSnapshotResolver"/> 单元测试（R3：快照是分支求值的唯一取数依据）。
/// </summary>
public sealed class ApprovalSnapshotResolverTests
{
    [Fact]
    public void IntegerNumberResolvesToLong()
    {
        var resolver = new ApprovalSnapshotResolver("""{"total_amount": 60000}""");
        var hit = resolver.TryResolve("total_amount", out var value);
        Assert.True(hit);
        // 显式类型断言：JSON 整数必须解析为 long（而非 int/double），保证金额类比较精度
        var typed = Assert.IsType<long>(value);
        Assert.Equal(60000L, typed);
    }

    [Fact]
    public void DecimalNumberResolvesToDouble()
    {
        var resolver = new ApprovalSnapshotResolver("""{"rate": 0.85}""");
        resolver.TryResolve("rate", out var value);
        Assert.Equal(0.85, value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BooleanResolvesToClrBool(bool raw)
    {
        var resolver = new ApprovalSnapshotResolver($$"""{"urgent": {{raw.ToString().ToLowerInvariant()}}}""");
        resolver.TryResolve("urgent", out var value);
        Assert.Equal(raw, value);
    }

    [Fact]
    public void StringResolvesToClrString()
    {
        var resolver = new ApprovalSnapshotResolver("""{"dept": "采购部"}""");
        resolver.TryResolve("dept", out var value);
        Assert.Equal("采购部", value);
    }

    [Fact]
    public void NullFieldResolvesTrueWithNullValue()
    {
        var resolver = new ApprovalSnapshotResolver("""{"remark": null}""");
        Assert.True(resolver.TryResolve("remark", out var value));
        Assert.Null(value);
    }

    [Fact]
    public void NameMatchingIsCaseInsensitive()
    {
        var resolver = new ApprovalSnapshotResolver("""{"TotalAmount": 1}""");
        Assert.True(resolver.TryResolve("totalamount", out _));
    }

    [Fact]
    public void MissingFieldReturnsFalse()
    {
        var resolver = new ApprovalSnapshotResolver("""{"other": 1}""");
        Assert.False(resolver.TryResolve("total_amount", out var value));
        Assert.Null(value);
    }

    [Fact]
    public void NonObjectSnapshotThrowsArgument()
        => Assert.Throws<ArgumentException>(() => new ApprovalSnapshotResolver("[1,2,3]"));

    [Fact]
    public void EmptyObjectIsValidAndResolvesNothing()
    {
        var resolver = new ApprovalSnapshotResolver("{}");
        Assert.False(resolver.TryResolve("anything", out _));
    }
}
