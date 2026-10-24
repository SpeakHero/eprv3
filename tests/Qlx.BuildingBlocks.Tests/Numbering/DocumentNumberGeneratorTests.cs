/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 描述：单据编号生成器单元测试（格式契约、同期流水递增、非法入参）
 */

using Microsoft.Extensions.Options;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Numbering;

namespace Qlx.BuildingBlocks.Tests.Numbering;

public sealed class DocumentNumberGeneratorTests
{
    private static DocumentNumberGenerator CreateGenerator(long? tenantId = 101)
        => new(new InMemorySerialSequenceStore(), new StubTenantContext(tenantId), Options.Create(new DocumentNumberingOptions()));

    [Fact]
    public async Task NextAsyncProducesExpectedFormat()
    {
        var generator = CreateGenerator();

        var number = await generator.NextAsync("CG", "001", TestContext.Current.CancellationToken);

        var expectedPeriod = DateTimeOffset.UtcNow.ToString("yyyyMM", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($"CG-001-{expectedPeriod}-000001", number);
    }

    [Fact]
    public async Task SerialIncrementsWithinSamePeriod()
    {
        var generator = CreateGenerator();
        var cancellationToken = TestContext.Current.CancellationToken;

        var first = await generator.NextAsync("CG", "001", cancellationToken);
        var second = await generator.NextAsync("CG", "001", cancellationToken);

        Assert.EndsWith("000001", first, StringComparison.Ordinal);
        Assert.EndsWith("000002", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SerialIsIndependentPerPrefixAndOrg()
    {
        var generator = CreateGenerator();
        var cancellationToken = TestContext.Current.CancellationToken;

        var purchase = await generator.NextAsync("CG", "001", cancellationToken);
        var sales = await generator.NextAsync("XS", "001", cancellationToken);
        var otherOrg = await generator.NextAsync("CG", "002", cancellationToken);

        Assert.EndsWith("000001", purchase, StringComparison.Ordinal);
        Assert.EndsWith("000001", sales, StringComparison.Ordinal);
        Assert.EndsWith("000001", otherOrg, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "001")]
    [InlineData("CG", "")]
    [InlineData("C:G", "001")]
    [InlineData("CG", "0:1")]
    public async Task InvalidSegmentsThrow(string prefix, string orgCode)
    {
        var generator = CreateGenerator();

        await Assert.ThrowsAsync<ArgumentException>(
            () => generator.NextAsync(prefix, orgCode, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TwoTenantsWithSameOrgDoNotCollide()
    {
        var store = new InMemorySerialSequenceStore();
        var options = Options.Create(new DocumentNumberingOptions());
        var tenantA = new DocumentNumberGenerator(store, new StubTenantContext(101), options);
        var tenantB = new DocumentNumberGenerator(store, new StubTenantContext(202), options);
        var cancellationToken = TestContext.Current.CancellationToken;

        var first = await tenantA.NextAsync("CG", "001", cancellationToken);
        var second = await tenantB.NextAsync("CG", "001", cancellationToken);

        Assert.EndsWith("000001", first, StringComparison.Ordinal);
        Assert.EndsWith("000001", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingTenantContextThrows()
    {
        var generator = CreateGenerator(tenantId: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => generator.NextAsync("CG", "001", TestContext.Current.CancellationToken));
    }

    /// <summary>租户上下文桩。</summary>
    private sealed class StubTenantContext(long? tenantId) : ITenantContext
    {
        private readonly long? _tenantId = tenantId;

        public string? TenantCode => _tenantId is null ? null : $"tenant-{_tenantId}";

        public long? TenantId { get; private set; } = tenantId;

        public bool IsResolved => TenantId is not null;

        public void SetTenantId(long tenantId) => TenantId = tenantId;

        public void SetTenantCode(string? tenantCode)
        {
        }
    }
}
