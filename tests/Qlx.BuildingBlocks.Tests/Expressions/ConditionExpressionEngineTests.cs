/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 描述：条件表达式引擎核心行为测试（算术 / 逻辑 / 比较 / 变量 / 函数 / 空值 / 短路 / 错误路径）
 */

using System.Globalization;
using Qlx.Abstractions.Expressions;

using Qlx.BuildingBlocks.Expressions;

namespace Qlx.BuildingBlocks.Tests;

/// <summary>引擎核心行为测试（仅内置能力，经工厂默认引擎执行）。</summary>
public class ConditionExpressionEngineTests
{
    private readonly IConditionExpressionEngine _engine = ConditionExpressionEngineFactory.CreateDefault();

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("10 / 4", 2.5)]
    [InlineData("10 % 3", 1)]
    [InlineData("-5 + 3", -2)]
    [InlineData("2 * (3 + 4) - 1", 13)]
    public void ArithmeticWithPrecedenceEvaluatesCorrectly(string expression, double expected)
    {
        var value = _engine.EvaluateValue(expression);
        Assert.Equal(Convert.ToDecimal(expected, CultureInfo.InvariantCulture), Convert.ToDecimal(value, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("'合计：' + 100", "合计：100")]
    [InlineData("'a' + 'b' + 'c'", "abc")]
    public void StringConcatUsesInvariantFormat(string expression, string expected)
    {
        var value = _engine.EvaluateValue(expression);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("true && true", true)]
    [InlineData("true && false", false)]
    [InlineData("true || false", true)]
    [InlineData("false || false", false)]
    [InlineData("!false", true)]
    [InlineData("NOT true", false)]
    [InlineData("!(1 > 2)", true)]
    [InlineData("1 < 2 && 2 <= 2 && 3 > 2 && 3 >= 3", true)]
    public void LogicalOperatorsEvaluateCorrectly(string expression, bool expected)
    {
        Assert.Equal(expected, _engine.Evaluate(expression));
    }

    [Theory]
    [InlineData("'abc' == 'abc'", true)]
    [InlineData("'abc' != 'abd'", true)]
    [InlineData("1 == 1.0", true)]
    [InlineData("2 > 1.5", true)]
    [InlineData("'B' > 'A'", true)]
    [InlineData("'a' < 'b'", true)]
    public void ComparisonOperatorsEvaluateCorrectly(string expression, bool expected)
    {
        Assert.Equal(expected, _engine.Evaluate(expression));
    }

    [Theory]
    [InlineData("null == null", true)]
    [InlineData("null != 1", true)]
    [InlineData("1 == null", false)]
    [InlineData("1 > null", false)]
    [InlineData("null > 1", false)]
    [InlineData("isNull(null)", true)]
    [InlineData("isNotNull(1)", true)]
    public void NullSemanticsFollowEngineContract(string expression, bool expected)
    {
        Assert.Equal(expected, _engine.Evaluate(expression));
    }

    [Fact]
    public void VariablesBareIdentifierAndParameterSyntaxResolveConsistently()
    {
        var variables = new DictionaryVariableResolver(
        [
            new KeyValuePair<string, object?>("status", "Draft"),
            new KeyValuePair<string, object?>("total_amount", 60000m)
        ]);

        Assert.True(_engine.Evaluate("status == 'Draft'", variables));
        Assert.True(_engine.Evaluate("${total_amount} > 50000", variables));
        Assert.True(_engine.Evaluate("total_amount == ${total_amount}", variables));
    }

    [Fact]
    public void VariablesListIsReportedByCompiledExpression()
    {
        var expression = _engine.Parse("a > 1 && b < 2 || ${c} == 3");
        Assert.Equal(["a", "b", "c"], expression.Variables);
    }

    [Fact]
    public void StrictModeMissingVariableThrowsEvaluateException()
    {
        var exception = Assert.Throws<ConditionExpressionException>(
            () => _engine.Evaluate("missing_var > 1"));
        Assert.Equal(ConditionExpressionErrorKind.Evaluate, exception.Kind);
    }

    [Fact]
    public void LenientModeMissingVariableTreatedAsNull()
    {
        var engine = ConditionExpressionEngineFactory.Create(options => options.ThrowOnUnresolvedVariable = false);
        Assert.True(engine.Evaluate("missing_var == null"));
        Assert.False(engine.Evaluate("missing_var > 1"));
    }

    [Fact]
    public void ShortCircuitSkipsRightOperandEvenInStrictMode()
    {
        // 左操作数已确定结果时，右操作数（含未解析变量）不会被求值
        Assert.False(_engine.Evaluate("false && missing_var > 1"));
        Assert.True(_engine.Evaluate("true || missing_var > 1"));
    }

    [Theory]
    [InlineData("between(amount, 1000, 5000)", 1000, true)]
    [InlineData("between(amount, 1000, 5000)", 3000, true)]
    [InlineData("between(amount, 1000, 5000)", 5000, true)]
    [InlineData("between(amount, 1000, 5000)", 999, false)]
    [InlineData("between(amount, 1000, 5000)", 5001, false)]
    [InlineData("!between(amount, 1000, 5000)", 9999, true)]
    public void BetweenFunctionIsInclusiveOnBothEnds(string expression, decimal amount, bool expected)
    {
        var variables = new DictionaryVariableResolver(
        [
            new KeyValuePair<string, object?>("amount", amount)
        ]);
        Assert.Equal(expected, _engine.Evaluate(expression, variables));
    }

    [Theory]
    [InlineData("biz_type in ['PURCHASE', 'EXPENSE']", "PURCHASE", true)]
    [InlineData("biz_type in ['PURCHASE', 'EXPENSE']", "SALES", false)]
    [InlineData("biz_type not in ['PURCHASE', 'EXPENSE']", "SALES", true)]
    public void InOperatorsMatchListMembership(string expression, string bizType, bool expected)
    {
        var variables = new DictionaryVariableResolver(
        [
            new KeyValuePair<string, object?>("biz_type", bizType)
        ]);
        Assert.Equal(expected, _engine.Evaluate(expression, variables));
    }

    [Fact]
    public void InOperatorAcceptsCollectionVariable()
    {
        var variables = new DictionaryVariableResolver(
        [
            new KeyValuePair<string, object?>("dept", "IT"),
            new KeyValuePair<string, object?>("allowed", new[] { "IT", "FIN" })
        ]);
        Assert.True(_engine.Evaluate("dept in allowed", variables));
    }

    [Fact]
    public void InOperatorWithRightStringThrows()
    {
        var exception = Assert.Throws<ConditionExpressionException>(
            () => _engine.Evaluate("'a' in 'abc'"));
        Assert.Equal(ConditionExpressionErrorKind.Evaluate, exception.Kind);
    }

    [Fact]
    public void TernarySelectsBranchByCondition()
    {
        Assert.Equal("big", _engine.EvaluateValue("amount > 100 ? 'big' : 'small'",
            new DictionaryVariableResolver([new KeyValuePair<string, object?>("amount", 500)])));
        Assert.Equal("small", _engine.EvaluateValue("amount > 100 ? 'big' : 'small'",
            new DictionaryVariableResolver([new KeyValuePair<string, object?>("amount", 50)])));
    }

    [Fact]
    public void StringEscapesQuoteAndControlCharactersAreSupported()
    {
        // 单双引号等价 + \n 转义还原
        Assert.True(_engine.Evaluate("'a\\nb' == \"a\\nb\""));
        // 双写引号转义（'' → '）
        Assert.True(_engine.Evaluate("'it''s' == \"it's\""));
        // \t 转义后长度为 3
        Assert.Equal(3, Convert.ToInt32(_engine.EvaluateValue("length('a\\tb')"), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NumericStringCoercionEnablesCrossTypeComparison()
    {
        Assert.True(_engine.Evaluate("'60000' > 50000"));
        Assert.True(_engine.Evaluate("'2026-06-01' > '2026-01-01'"));
    }

    [Fact]
    public void DateVariableComparedWithStringDateUsesCoercion()
    {
        var variables = new DictionaryVariableResolver(
        [
            new KeyValuePair<string, object?>("createdAt", new DateTime(2026, 6, 1))
        ]);
        Assert.True(_engine.Evaluate("createdAt > '2026-01-01'", variables));
        Assert.False(_engine.Evaluate("createdAt > '2026-12-31'", variables));
    }

    [Fact]
    public void DateSubtractionReturnsTimeSpan()
    {
        var variables = new DictionaryVariableResolver(
        [
            new KeyValuePair<string, object?>("start", new DateTime(2026, 1, 1)),
            new KeyValuePair<string, object?>("end", new DateTime(2026, 1, 11))
        ]);
        var value = Assert.IsType<TimeSpan>(_engine.EvaluateValue("end - start", variables));
        Assert.Equal(10, value.TotalDays);
    }

    [Fact]
    public void EnumVariableComparedWithStringAndNumberUsesEnumSemantics()
    {
        var variables = new DictionaryVariableResolver(
        [
            new KeyValuePair<string, object?>("status", SampleStatus.Submitted)
        ]);
        Assert.True(_engine.Evaluate("status == 'Submitted'", variables));
        Assert.True(_engine.Evaluate("status == 1", variables));
        Assert.False(_engine.Evaluate("status == 'Draft'", variables));
    }

    [Theory]
    [InlineData("contains('采购申请单', '采购')", true)]
    [InlineData("startsWith('PR-2026-001', 'PR-')", true)]
    [InlineData("endsWith('report.xlsx', '.docx')", false)]
    [InlineData("length('abcd')", 4)]
    [InlineData("abs(-5) + abs(3)", 8)]
    [InlineData("min(3, 1, 2)", 1)]
    [InlineData("max(3, 1, 2)", 3)]
    [InlineData("round(3.14159, 2)", 3.14)]
    [InlineData("floor(3.9)", 3)]
    [InlineData("ceiling(3.1)", 4)]
    [InlineData("upper('abc')", "ABC")]
    [InlineData("lower('ABC')", "abc")]
    [InlineData("trim('  x  ')", "x")]
    [InlineData("substring('abcdef', 1, 3)", "bcd")]
    [InlineData("if(1 > 0, 'yes', 'no')", "yes")]
    [InlineData("coalesce(null, null, 'x')", "x")]
    public void BuiltInFunctionsEvaluateCorrectly(string expression, object expected)
    {
        var value = _engine.EvaluateValue(expression);
        Assert.Equal(Convert.ToString(expected, System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NowAndTodayReturnServerLocalTime()
    {
        Assert.IsType<DateTime>(_engine.EvaluateValue("now()"));
        Assert.Equal(DateTime.Today, Assert.IsType<DateTime>(_engine.EvaluateValue("today()")));
    }

    [Fact]
    public void DivisionByZeroThrowsEvaluateException()
    {
        Assert.Equal(ConditionExpressionErrorKind.Evaluate,
            Assert.Throws<ConditionExpressionException>(() => _engine.Evaluate("1 / 0")).Kind);
        Assert.Equal(ConditionExpressionErrorKind.Evaluate,
            Assert.Throws<ConditionExpressionException>(() => _engine.Evaluate("5 % 0")).Kind);
    }

    [Fact]
    public void NonBooleanConditionResultThrowsEvaluateException()
    {
        var exception = Assert.Throws<ConditionExpressionException>(() => _engine.Evaluate("1 + 1"));
        Assert.Equal(ConditionExpressionErrorKind.Evaluate, exception.Kind);
    }

    [Theory]
    [InlineData("1 +")]
    [InlineData("(1 + 2")]
    [InlineData("1 2")]
    [InlineData("'unclosed")]
    [InlineData("123abc")]
    [InlineData("@")]
    [InlineData("1 ? 2")]
    [InlineData("unknown_fn(1)")]
    [InlineData("1; 2")]
    public void ParseErrorsThrowParseKindWithPosition(string expression)
    {
        var exception = Assert.Throws<ConditionExpressionException>(() => _engine.Parse(expression));
        Assert.Equal(ConditionExpressionErrorKind.Parse, exception.Kind);
        Assert.NotNull(exception.Position);
    }

    [Fact]
    public void ExcessiveNestingThrowsDepthLimitException()
    {
        var engine = ConditionExpressionEngineFactory.Create(options => options.MaxDepth = 8);
        var expression = string.Concat(Enumerable.Repeat("(", 20)) + "1" + string.Concat(Enumerable.Repeat(")", 20));
        var exception = Assert.Throws<ConditionExpressionException>(() => engine.Parse(expression));
        Assert.Equal(ConditionExpressionErrorKind.Parse, exception.Kind);
    }

    [Fact]
    public void ExcessiveLengthThrowsLengthLimitException()
    {
        var engine = ConditionExpressionEngineFactory.Create(options => options.MaxExpressionLength = 16);
        var exception = Assert.Throws<ConditionExpressionException>(() => engine.Parse("1 + 1 + 1 + 1 + 1 + 1 + 1"));
        Assert.Equal(ConditionExpressionErrorKind.Parse, exception.Kind);
    }

    [Fact]
    public void CompiledExpressionIsReusableAcrossCalls()
    {
        var compiled = _engine.Parse("total_amount > 50000 && status == 'Draft'");
        var draft = new DictionaryVariableResolver(
        [
            new KeyValuePair<string, object?>("total_amount", 60000m),
            new KeyValuePair<string, object?>("status", "Draft")
        ]);
        var approved = new DictionaryVariableResolver(
        [
            new KeyValuePair<string, object?>("total_amount", 60000m),
            new KeyValuePair<string, object?>("status", "Approved")
        ]);

        Assert.True(_engine.Evaluate(compiled, draft));
        Assert.False(_engine.Evaluate(compiled, approved));
    }

    [Fact]
    public void TypedVariableWithCaseInsensitiveResolverMatchesAnyCasing()
    {
        var variables = new DictionaryVariableResolver(
        [
            new KeyValuePair<string, object?>("TotalAmount", 100m)
        ]);
        Assert.True(_engine.Evaluate("totalamount > 50", variables));
    }

    private enum SampleStatus
    {
        Draft = 0,
        Submitted = 1
    }
}
