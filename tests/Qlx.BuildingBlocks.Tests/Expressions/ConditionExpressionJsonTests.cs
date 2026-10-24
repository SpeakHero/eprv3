/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 描述：条件表达式 expr_json 互转测试（$type 判别符、标量字面量类型、往返语义等价、非法载荷防护）
 */

using Qlx.Abstractions.Expressions;

using Qlx.BuildingBlocks.Expressions;

namespace Qlx.BuildingBlocks.Tests;

/// <summary>expr_json 序列化 / 反序列化测试：验证表达式制品可安全持久化（审批中心 expr_json），反序列化后等价求值。</summary>
public class ConditionExpressionJsonTests
{
    private readonly IConditionExpressionEngine _engine = ConditionExpressionEngineFactory.CreateDefault();

    [Fact]
    public void ToJsonContainsTypeDiscriminatorAndCamelCaseProperties()
    {
        var expression = _engine.Parse("amount > 100");
        var json = expression.ToJson();

        Assert.Contains("\"$type\":\"binary\"", json, StringComparison.Ordinal);
        Assert.Contains("\"$type\":\"variable\"", json, StringComparison.Ordinal);
        Assert.Contains("\"$type\":\"literal\"", json, StringComparison.Ordinal);
        // 平台统一 JSON 选项的默认编码器对 > / < / & 做 \uXXXX 转义（语义等价，反序列化后还原）
        Assert.Contains("\"operatorSymbol\":\"\\u003E\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonRoundTripPreservesEvaluationSemantics()
    {
        var expression = _engine.Parse("total_amount > 50000 && status == 'Draft' && biz_type in ['PURCHASE', 'EXPENSE']");
        var restored = ConditionExpression.FromJson(expression.ToJson());

        Assert.Equal(["total_amount", "status", "biz_type"], restored.Variables);
        Assert.Equal(string.Empty, restored.SourceText);

        var draft = new DictionaryVariableResolver(
        [
            new KeyValuePair<string, object?>("total_amount", 60000m),
            new KeyValuePair<string, object?>("status", "Draft"),
            new KeyValuePair<string, object?>("biz_type", "PURCHASE")
        ]);
        var approved = new DictionaryVariableResolver(
        [
            new KeyValuePair<string, object?>("total_amount", 60000m),
            new KeyValuePair<string, object?>("status", "Approved"),
            new KeyValuePair<string, object?>("biz_type", "PURCHASE")
        ]);
        var expense = new DictionaryVariableResolver(
        [
            new KeyValuePair<string, object?>("total_amount", 1000m),
            new KeyValuePair<string, object?>("status", "Draft"),
            new KeyValuePair<string, object?>("biz_type", "TRAVEL")
        ]);

        Assert.True(_engine.Evaluate(restored, draft));
        Assert.False(_engine.Evaluate(restored, approved));
        Assert.False(_engine.Evaluate(restored, expense));
    }

    [Fact]
    public void JsonRoundTripPreservesScalarLiteralTypes()
    {
        var expression = _engine.Parse("count == 3 && ratio == 0.5 && name == 'PR-01' && flag == true && missing == null");
        var restored = ConditionExpression.FromJson(expression.ToJson());

        var variables = new DictionaryVariableResolver(
        [
            new KeyValuePair<string, object?>("count", 3),
            new KeyValuePair<string, object?>("ratio", 0.5m),
            new KeyValuePair<string, object?>("name", "PR-01"),
            new KeyValuePair<string, object?>("flag", true),
            new KeyValuePair<string, object?>("missing", null)
        ]);

        Assert.True(_engine.Evaluate(restored, variables));
    }

    [Fact]
    public void JsonRoundTripUnaryTernaryAndFunctionNodesEvaluateCorrectly()
    {
        var expression = _engine.Parse("!(amount > 100) ? 'small' : Upper(name)");
        var restored = ConditionExpression.FromJson(expression.ToJson());

        var small = new DictionaryVariableResolver(
        [
            new KeyValuePair<string, object?>("amount", 50),
            new KeyValuePair<string, object?>("name", "abc")
        ]);
        var large = new DictionaryVariableResolver(
        [
            new KeyValuePair<string, object?>("amount", 500),
            new KeyValuePair<string, object?>("name", "abc")
        ]);

        Assert.Equal("small", _engine.EvaluateValue(restored, small));
        Assert.Equal("ABC", _engine.EvaluateValue(restored, large));
    }

    [Fact]
    public void JsonRoundTripIsIdempotentAcrossRepeatedCycles()
    {
        var first = _engine.Parse("1 + 2 * 3").ToJson();
        var second = ConditionExpression.FromJson(first).ToJson();
        var third = ConditionExpression.FromJson(second).ToJson();

        Assert.Equal(second, third);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"$type\":\"unknown\"}")]
    [InlineData("123")]
    public void FromJsonInvalidPayloadThrowsParseKind(string json)
    {
        var exception = Assert.Throws<ConditionExpressionException>(() => ConditionExpression.FromJson(json));
        Assert.Equal(ConditionExpressionErrorKind.Parse, exception.Kind);
    }
}
