/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 描述：条件表达式引擎扩展机制测试（自定义运算符/函数/字面量/转换器、冲突保护、默认引擎隔离、DI 装配）
 */

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Qlx.Abstractions.Expressions;

using Qlx.BuildingBlocks.Expressions;

namespace Qlx.BuildingBlocks.Tests;

/// <summary>扩展机制测试：验证业务方可在不修改引擎核心的前提下注入自定义能力，且互不产生副作用。</summary>
public class ConditionExpressionExtensionTests
{
    [Fact]
    public void CustomBinaryOperatorWithRightAssociativityFollowsPrecedence()
    {
        // 幂运算 ** ：右结合（2 ** 3 ** 2 = 2 ** 9 = 512）；显式括号控制与乘法的结合顺序
        var engine = ConditionExpressionEngineFactory.Create(options =>
        {
            options.RegisterBinaryOperator(new PowerOperator());
        });

        Assert.Equal(512m, Convert.ToDecimal(engine.EvaluateValue("2 ** 3 ** 2"), CultureInfo.InvariantCulture));
        Assert.Equal(32m, Convert.ToDecimal(engine.EvaluateValue("(2 ** 3) * 4"), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void CustomUnaryOperatorIsAppliedBeforeBinaryOperators()
    {
        var engine = ConditionExpressionEngineFactory.Create(options =>
        {
            options.RegisterUnaryOperator(new SquareRootOperator());
        });

        Assert.True(engine.Evaluate("√9 == 3"));
        Assert.True(engine.Evaluate("√(4 + 5) == 3"));
    }

    [Fact]
    public void CustomFunctionExtendsFunctionLibrary()
    {
        var engine = ConditionExpressionEngineFactory.Create(options =>
        {
            options.RegisterFunction(new AddDaysFunction());
        });

        var variables = new DictionaryVariableResolver(
        [
            new KeyValuePair<string, object?>("orderDate", new DateTime(2026, 10, 20))
        ]);
        var value = Assert.IsType<DateTime>(engine.EvaluateValue("AddDays(orderDate, 5)", variables));
        Assert.Equal(new DateTime(2026, 10, 25), value);
    }

    [Fact]
    public void CustomLiteralParserTakesEffectBeforeBuiltInParsing()
    {
        var engine = ConditionExpressionEngineFactory.Create(options =>
        {
            options.RegisterLiteralParser(new HexLiteralParser());
        });

        Assert.True(engine.Evaluate("0x1F == 31"));
        Assert.True(engine.Evaluate("0xFF == 255"));
        // 未命中解析器的词素仍走内置解析
        Assert.True(engine.Evaluate("0x10 + 6 == 22"));
    }

    [Fact]
    public void CustomValueCoercerEnablesDomainValueComparison()
    {
        var engine = ConditionExpressionEngineFactory.Create(options =>
        {
            options.RegisterValueCoercer(new MoneyCoercer());
        });

        var variables = new DictionaryVariableResolver(
        [
            new KeyValuePair<string, object?>("price", new Money(150m))
        ]);
        Assert.True(engine.Evaluate("price > 100", variables));
        Assert.False(engine.Evaluate("price > 200", variables));
    }

    [Fact]
    public void ExtensionFacadeRegistersAllCapabilitiesInOneShot()
    {
        var engine = ConditionExpressionEngineFactory.Create(new PurchaseConditionExtension());

        Assert.True(engine.Evaluate("amount > 5000 && risk_level == 'HIGH'",
            new DictionaryVariableResolver(
            [
                new KeyValuePair<string, object?>("amount", 6000m),
                new KeyValuePair<string, object?>("risk_level", "HIGH")
            ])));
    }

    [Fact]
    public void DuplicateRegistrationWithoutOverrideThrowsRegistryException()
    {
        var exception = Assert.Throws<ConditionExpressionException>(() =>
            ConditionExpressionEngineFactory.Create(options =>
            {
                options.RegisterBinaryOperator(new PowerOperator());
                options.RegisterBinaryOperator(new PowerOperator());
            }));
        Assert.Equal(ConditionExpressionErrorKind.Registry, exception.Kind);
    }

    [Fact]
    public void OverrideBuiltInWithExplicitAllowReplacesBehaviorLocally()
    {
        var engine = ConditionExpressionEngineFactory.Create(options =>
        {
            options.RegisterFunction(new IsNullFunction(), allowOverride: true);
        });

        // 覆盖后的 IsNull 把空字符串也视为空（局部语义），默认引擎不受影响
        Assert.True(engine.Evaluate("isNull('')"));
        Assert.False(ConditionExpressionEngineFactory.Default.Evaluate("isNull('')"));
    }

    [Fact]
    public void DefaultEngineIsUnaffectedByCustomEngines()
    {
        _ = ConditionExpressionEngineFactory.Create(new PurchaseConditionExtension());

        // 默认引擎不认识扩展运算符
        Assert.Throws<ConditionExpressionException>(
            () => ConditionExpressionEngineFactory.Default.Evaluate("2 ** 3"));
        // 默认引擎内置能力不受影响
        Assert.True(ConditionExpressionEngineFactory.Default.Evaluate("1 < 2 && 'a' != 'b'"));
    }

    [Fact]
    public async Task DependencyInjectionRegistersSingletonEngineWithExtensions()
    {
        var services = new ServiceCollection();
        services.AddQlxConditionExpressions(options => options.RegisterFunction(new AddDaysFunction()));

        await using var provider = services.BuildServiceProvider();
        var engine1 = provider.GetRequiredService<IConditionExpressionEngine>();
        var engine2 = provider.GetRequiredService<IConditionExpressionEngine>();

        Assert.Same(engine1, engine2);
        Assert.True(engine1.Evaluate("AddDays('2026-10-20', 1) > '2026-10-20'"));
    }

    [Fact]
    public async Task DependencyInjectionProvidesServicesToCustomFunction()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IThresholdProvider, FixedThresholdProvider>();
        services.AddQlxConditionExpressions((options, _) => options.RegisterFunction(new ThresholdFunction()));

        await using var provider = services.BuildServiceProvider();
        var engine = provider.GetRequiredService<IConditionExpressionEngine>();

        Assert.True(engine.Evaluate("amount > GetThreshold('PURCHASE')",
            new DictionaryVariableResolver([new KeyValuePair<string, object?>("amount", 100m)])));
    }

    /// <summary>幂运算（右结合，测试用自定义二元运算符）。</summary>
    private sealed record PowerOperator : IConditionBinaryOperator
    {
        public string Symbol => "**";

        public IEnumerable<string> Aliases => [];

        public ConditionPrecedence Precedence => ConditionPrecedence.Multiplicative;

        public bool IsRightAssociative => true;

        public object? Apply(object? left, object? right, IConditionEvaluationContext context)
            => (decimal)Math.Pow(
                Convert.ToDouble(left, CultureInfo.InvariantCulture),
                Convert.ToDouble(right, CultureInfo.InvariantCulture));
    }

    /// <summary>平方根（测试用自定义一元运算符）。</summary>
    private sealed record SquareRootOperator : IConditionUnaryOperator
    {
        public string Symbol => "√";

        public IEnumerable<string> Aliases => [];

        public object? Apply(object? operand, IConditionEvaluationContext context)
            => Math.Sqrt(Convert.ToDouble(operand, CultureInfo.InvariantCulture));
    }

    /// <summary>加天数（测试用自定义函数）。</summary>
    private sealed record AddDaysFunction : IConditionFunction
    {
        public string Name => "AddDays";

        public int MinArgumentCount => 2;

        public int MaxArgumentCount => 2;

        public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
        {
            var date = arguments[0] switch
            {
                DateTime dt => dt,
                string text => DateTime.Parse(text, CultureInfo.InvariantCulture),
                _ => throw new ConditionExpressionException(ConditionExpressionErrorKind.Evaluate, "AddDays 第一个参数必须是日期。")
            };
            return date.AddDays(Convert.ToDouble(arguments[1], CultureInfo.InvariantCulture));
        }
    }

    /// <summary>空值判断（测试用覆盖内置 IsNull：把空字符串也视为空）。</summary>
    private sealed record IsNullFunction : IConditionFunction
    {
        public string Name => "IsNull";

        public int MinArgumentCount => 1;

        public int MaxArgumentCount => 1;

        public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
            => arguments[0] is null || (arguments[0] is string text && text.Length == 0);
    }

    /// <summary>十六进制字面量解析器（0x1F → 31）。</summary>
    private sealed class HexLiteralParser : IConditionLiteralParser
    {
        public bool TryParse(ConditionLiteralTokenKind tokenKind, string text, out object? value)
        {
            if (tokenKind == ConditionLiteralTokenKind.Number
                && text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            {
                value = hex;
                return true;
            }

            value = null;
            return false;
        }
    }

    /// <summary>领域金额值（测试用自定义类型）。</summary>
    private sealed record Money(decimal Amount);

    /// <summary>Money → decimal 值转换器（测试用）。</summary>
    private sealed class MoneyCoercer : IConditionValueCoercer
    {
        public bool TryConvert(object? value, ConditionValueKind target, out object? converted)
        {
            if (target == ConditionValueKind.Numeric && value is Money money)
            {
                converted = money.Amount;
                return true;
            }

            converted = null;
            return false;
        }
    }

    /// <summary>采购域扩展门面（测试用）。</summary>
    private sealed class PurchaseConditionExtension : IConditionExtension
    {
        public void Configure(IConditionRegistry options)
        {
            options.RegisterLiteralParser(new HexLiteralParser());
            options.RegisterBinaryOperator(new PowerOperator());
        }
    }

    /// <summary>阈值提供程序（测试用 DI 服务）。</summary>
    private interface IThresholdProvider
    {
        decimal Get(string scope);
    }

    private sealed class FixedThresholdProvider : IThresholdProvider
    {
        public decimal Get(string scope) => 50m;
    }

    /// <summary>经上下文访问 DI 服务的自定义函数。</summary>
    private sealed record ThresholdFunction : IConditionFunction
    {
        public string Name => "GetThreshold";

        public int MinArgumentCount => 1;

        public int MaxArgumentCount => 1;

        public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
        {
            var provider = context.Services?.GetService(typeof(IThresholdProvider)) as IThresholdProvider
                ?? throw new ConditionExpressionException(ConditionExpressionErrorKind.Evaluate, "阈值服务未注册。");
            return provider.Get(Convert.ToString(arguments[0], CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }
}
