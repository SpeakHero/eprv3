/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 描述：条件表达式性能压测测试（解析吞吐 / 编译制品求值吞吐 / 文本求值 / 大表达式 / 深嵌套）
 */

using System.Diagnostics;
using System.Text;
using Qlx.Abstractions.Expressions;

using Qlx.BuildingBlocks.Expressions;

namespace Qlx.BuildingBlocks.Tests;

/// <summary>
/// 性能压测测试：验证解析与求值吞吐满足审批条件场景需求。
/// 预算为 CI 环境宽松上限（实际耗时通常为预算的十分之一量级），
/// 用于防止数量级性能退化，而非精确回归门禁。
/// </summary>
public class ConditionExpressionPerformanceTests
{
    private readonly IConditionExpressionEngine _engine = ConditionExpressionEngineFactory.CreateDefault();

    [Fact]
    public void ParseSimpleConditionThroughputWithinBudget()
    {
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 10_000; i++)
        {
            _ = _engine.Parse("total_amount > 50000 && status == 'Draft'");
        }

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"解析 10000 次耗时 {stopwatch.ElapsedMilliseconds}ms，超出 5s 预算。");
    }

    [Fact]
    public void EvaluateCompiledConditionThroughputWithinBudget()
    {
        var compiled = _engine.Parse("total_amount > 50000 && status == 'Draft' && contains(remark, '加急')");
        var variables = new DictionaryVariableResolver(
        [
            new KeyValuePair<string, object?>("total_amount", 60000m),
            new KeyValuePair<string, object?>("status", "Draft"),
            new KeyValuePair<string, object?>("remark", "采购加急单")
        ]);

        var stopwatch = Stopwatch.StartNew();
        var matched = 0;
        for (var i = 0; i < 100_000; i++)
        {
            if (_engine.Evaluate(compiled, variables))
            {
                matched++;
            }
        }

        stopwatch.Stop();
        Assert.Equal(100_000, matched);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"编译制品求值 100000 次耗时 {stopwatch.ElapsedMilliseconds}ms，超出 5s 预算。");
    }

    [Fact]
    public void EvaluateFromRawTextIncludingParseCostWithinBudget()
    {
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 5_000; i++)
        {
            _ = _engine.EvaluateValue("amount > 100 ? Between(amount, 100, 500) : false",
                new DictionaryVariableResolver([new KeyValuePair<string, object?>("amount", 200m)]));
        }

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"文本求值（含解析）5000 次耗时 {stopwatch.ElapsedMilliseconds}ms，超出 5s 预算。");
    }

    [Fact]
    public void ParseAndEvaluateLargeConditionWithinBudget()
    {
        const int clauseCount = 300;
        StringBuilder builder = new();
        var pairs = new KeyValuePair<string, object?>[clauseCount];
        for (var i = 0; i < clauseCount; i++)
        {
            if (i > 0)
            {
                builder.Append(" && ");
            }

            builder.Append("var_").Append(i).Append(" > ").Append(i);
            pairs[i] = new($"var_{i}", (decimal)(i + 1));
        }

        var text = builder.ToString();
        Assert.True(text.Length < 8192, "压测表达式长度超出默认 MaxExpressionLength，请调小子句数量。");

        var parseStopwatch = Stopwatch.StartNew();
        var compiled = _engine.Parse(text);
        parseStopwatch.Stop();

        var variables = new DictionaryVariableResolver(pairs);
        var evaluateStopwatch = Stopwatch.StartNew();
        var matched = 0;
        for (var i = 0; i < 1_000; i++)
        {
            if (_engine.Evaluate(compiled, variables))
            {
                matched++;
            }
        }

        evaluateStopwatch.Stop();

        Assert.Equal(1_000, matched);
        Assert.True(parseStopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"大表达式（{clauseCount} 子句）解析耗时 {parseStopwatch.ElapsedMilliseconds}ms，超出 2s 预算。");
        Assert.True(evaluateStopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"大表达式求值 1000 次耗时 {evaluateStopwatch.ElapsedMilliseconds}ms，超出 5s 预算。");
    }

    [Fact]
    public void ParseDeeplyNestedConditionWithinBudget()
    {
        // 30 层括号嵌套（默认 MaxDepth 64 之内：每层括号占 2 层递归深度）
        var text = string.Concat(Enumerable.Repeat("(", 30)) + "1" + string.Concat(Enumerable.Repeat(")", 30));

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++)
        {
            _ = _engine.Parse(text);
        }

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"深嵌套表达式解析 100 次耗时 {stopwatch.ElapsedMilliseconds}ms，超出 2s 预算。");
    }
}
