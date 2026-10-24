/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 04:40:00 +08:00 中国标准时间 星期五
 * 描述：重试延迟计算器测试——固定 / 线性 / 指数退避与最大延迟封顶
 */

using Qlx.Abstractions.Jobs;
using Qlx.BuildingBlocks.Jobs;

namespace Qlx.BuildingBlocks.Tests.Jobs;

/// <summary>
/// <see cref="RetryDelayCalculator"/> 单元测试。方案 D19：默认指数 2^n 退避并按 MaxDelaySeconds 封顶。
/// </summary>
public sealed class RetryDelayCalculatorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void ComputeDelayFixedBackoffIgnoresAttempt(int attempt)
    {
        var policy = new RetryPolicy
        {
            MaxRetries = 3,
            InitialIntervalSeconds = 60,
            Backoff = RetryBackoffStrategy.Fixed,
            MaxDelaySeconds = 3600,
        };

        var delay = RetryDelayCalculator.ComputeDelay(policy, attempt);

        Assert.Equal(TimeSpan.FromSeconds(60), delay);
    }

    [Fact]
    public void ComputeDelayLinearBackoffScalesWithAttempt()
    {
        var policy = new RetryPolicy
        {
            InitialIntervalSeconds = 10,
            Backoff = RetryBackoffStrategy.Linear,
            MaxDelaySeconds = 3600,
        };

        Assert.Equal(TimeSpan.FromSeconds(10), RetryDelayCalculator.ComputeDelay(policy, 1));
        Assert.Equal(TimeSpan.FromSeconds(20), RetryDelayCalculator.ComputeDelay(policy, 2));
        Assert.Equal(TimeSpan.FromSeconds(30), RetryDelayCalculator.ComputeDelay(policy, 3));
    }

    [Fact]
    public void ComputeDelayExponentialBackoffDoublesPerAttempt()
    {
        var policy = new RetryPolicy
        {
            InitialIntervalSeconds = 60,
            Backoff = RetryBackoffStrategy.Exponential,
            MaxDelaySeconds = 3600,
        };

        Assert.Equal(TimeSpan.FromSeconds(60), RetryDelayCalculator.ComputeDelay(policy, 1));
        Assert.Equal(TimeSpan.FromSeconds(120), RetryDelayCalculator.ComputeDelay(policy, 2));
        Assert.Equal(TimeSpan.FromSeconds(240), RetryDelayCalculator.ComputeDelay(policy, 3));
    }

    [Fact]
    public void ComputeDelayExponentialBackoffCappedAtMaxDelay()
    {
        var policy = new RetryPolicy
        {
            InitialIntervalSeconds = 3600,
            Backoff = RetryBackoffStrategy.Exponential,
            MaxDelaySeconds = 3600,
        };

        // 3600 * 2^9 已远超上限，必须封顶为 MaxDelaySeconds
        var delay = RetryDelayCalculator.ComputeDelay(policy, 10);

        Assert.Equal(TimeSpan.FromSeconds(3600), delay);
    }

    [Fact]
    public void ComputeDelayContractDefaultsUseExponentialSixtySeconds()
    {
        // 契约默认策略：MaxRetries=3 / Initial=60s / Exponential / MaxDelay=1h
        var delay = RetryDelayCalculator.ComputeDelay(new RetryPolicy(), 1);

        Assert.Equal(TimeSpan.FromSeconds(60), delay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ComputeDelayAttemptBelowOneThrows(int attempt)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RetryDelayCalculator.ComputeDelay(new RetryPolicy(), attempt));
    }
}
