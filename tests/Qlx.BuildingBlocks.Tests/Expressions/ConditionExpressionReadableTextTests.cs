/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期三
 * 描述：条件表达式可读文本（expr_text）渲染测试（最小括号、词运算符归一、字面量转义、JSON 路径保守括号）
 */

using System.Globalization;
using Qlx.Abstractions.Expressions;
using Qlx.BuildingBlocks.Expressions;

namespace Qlx.BuildingBlocks.Tests;

/// <summary>可读文本渲染测试：验证渲染器生成语义等价、最小括号的规范化表达式文本。</summary>
public class ConditionExpressionReadableTextTests
{
    private readonly IConditionExpressionEngine _engine = ConditionExpressionEngineFactory.CreateDefault();

    [Theory]
    [InlineData("1 + 2 * 3", "1 + 2 * 3")]
    [InlineData("(1 + 2) * 3", "(1 + 2) * 3")]
    [InlineData("a - (b - c)", "a - (b - c)")]
    [InlineData("a - b - c", "a - b - c")]
    [InlineData("!(1 > 2)", "!(1 > 2)")]
    [InlineData("-amount", "-amount")]
    [InlineData("-(1 + 2)", "-(1 + 2)")]
    [InlineData("a > 1 ? 'big' : 'small'", "a > 1 ? 'big' : 'small'")]
    public void ReadableTextUsesMinimalParentheses(string source, string expected)
    {
        var expr = _engine.Parse(source);
        Assert.Equal(expected, ConditionNodeTextRenderer.Render(expr.Root));
    }

    [Fact]
    public void ReadableTextNormalizesWordOperatorsAndFunctionNames()
    {
        Assert.Equal("a && b || c", ConditionNodeTextRenderer.Render(_engine.Parse("a AND b OR c").Root));
        Assert.Equal("biz_type IN ['PURCHASE', 'EXPENSE']", ConditionNodeTextRenderer.Render(_engine.Parse("biz_type in ['PURCHASE', 'EXPENSE']").Root));
        Assert.Equal("biz_type NOT IN ['PURCHASE']", ConditionNodeTextRenderer.Render(_engine.Parse("biz_type not in ['PURCHASE']").Root));
        Assert.Equal("!flag", ConditionNodeTextRenderer.Render(_engine.Parse("NOT flag").Root));
        Assert.Equal("Between(amount, 1000, 5000)", ConditionNodeTextRenderer.Render(_engine.Parse("between(amount, 1000, 5000)").Root));
        Assert.Equal("IsNull(missing)", ConditionNodeTextRenderer.Render(_engine.Parse("isNull(missing)").Root));
    }

    [Fact]
    public void ReadableTextEscapesStringLiterals()
    {
        Assert.Equal("'it\\'s'", ConditionNodeTextRenderer.Render(_engine.Parse("'it''s'").Root));
    }

    [Fact]
    public void ReadableTextAfterJsonRoundTripUsesConservativeParentheses()
    {
        // JSON 反序列化路径运算符优先级未知，渲染器对嵌套二元节点保守加括号（保证语义等价）
        var restored = ConditionExpression.FromJson(_engine.Parse("1 + 2 * 3").ToJson());

        Assert.Equal("1 + (2 * 3)", ConditionNodeTextRenderer.Render(restored.Root));
        Assert.Equal(7m, Convert.ToDecimal(_engine.EvaluateValue(restored), CultureInfo.InvariantCulture));
    }
}
