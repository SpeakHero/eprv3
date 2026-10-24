/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 描述：引擎组合期配置（扩展注册入口 + 运行时开关），构建期完成冲突校验，运行期不可变
 */

using Qlx.Abstractions.Expressions;

namespace Qlx.BuildingBlocks.Expressions;

/// <summary>
/// 条件表达式引擎组合期配置。本类型是扩展注册的唯一入口：
/// 业务方经 <see cref="IConditionExtension"/> 或工厂回调在本类型上注册
/// 自定义二元/一元运算符、自定义函数、自定义字面量解析器与值强制转换器；
/// 全部注册发生在引擎实例创建之前（组合期），引擎创建后配置不可再变更，
/// 引擎运行期完全不可变，保证扩展代码对核心引擎无运行时副作用。
/// </summary>
/// <remarks>
/// 冲突保护：注册符号 / 函数名与既有注册（含内置）重复时抛出
/// <see cref="ConditionExpressionException"/>（Kind = Registry），
/// 仅在显式声明 allowOverride = true 时允许覆盖——内置能力因此始终保持向后兼容。
/// </remarks>
public sealed class ConditionEngineOptions : IConditionRegistry
{
    private readonly List<IConditionBinaryOperator> _binaryOperators = [.. BuiltInOperators.Binary];
    private readonly List<IConditionUnaryOperator> _unaryOperators = [.. BuiltInOperators.Unary];
    private readonly List<IConditionFunction> _functions = [.. BuiltInFunctions.All];
    private readonly List<IConditionLiteralParser> _literalParsers = [];
    private readonly List<IConditionValueCoercer> _valueCoercers = [BuiltInValueCoercer.Shared];

    /// <summary>表达式最大字符长度（防超长输入 DoS），默认 8192。</summary>
    public int MaxExpressionLength { get; set; } = 8192;

    /// <summary>表达式最大嵌套深度（防深递归栈溢出），默认 64。</summary>
    public int MaxDepth { get; set; } = 64;

    /// <summary>
    /// 变量未解析时是否抛出求值异常。默认 true（审批条件场景缺变量视为数据错误应显式失败）；
    /// 置为 false 时未解析变量按 null 参与运算。
    /// </summary>
    public bool ThrowOnUnresolvedVariable { get; set; } = true;

    /// <summary>
    /// 引擎级服务提供程序：自定义函数 / 运算符经 <see cref="ConditionEvaluationContext.Services"/> 访问宿主服务。
    /// DI 注册路径由宿主自动注入，手工构建引擎时可选。
    /// </summary>
    public IServiceProvider? Services { get; set; }

    /// <summary>
    /// 注册自定义二元运算符。
    /// </summary>
    /// <param name="binaryOperator">运算符实例（必须线程安全、无状态）。</param>
    /// <param name="allowOverride">是否允许覆盖同名符号的既有注册（含内置），默认 false。</param>
    /// <returns>当前配置（链式调用）。</returns>
    /// <exception cref="ConditionExpressionException">符号为空、符号重复且未允许覆盖时抛出。</exception>
    public ConditionEngineOptions RegisterBinaryOperator(IConditionBinaryOperator binaryOperator, bool allowOverride = false)
    {
        ArgumentNullException.ThrowIfNull(binaryOperator);
        ValidateSymbol(binaryOperator.Symbol);
        ValidateAliases(binaryOperator);
        EnsureBinarySymbolAvailable(binaryOperator.Symbol, binaryOperator, allowOverride);
        foreach (var alias in binaryOperator.Aliases)
        {
            EnsureBinarySymbolAvailable(alias, binaryOperator, allowOverride);
        }

        _binaryOperators.Add(binaryOperator);
        return this;
    }

    /// <summary>
    /// 注册自定义一元（前缀）运算符。
    /// </summary>
    /// <param name="unaryOperator">运算符实例（必须线程安全、无状态）。</param>
    /// <param name="allowOverride">是否允许覆盖同名符号的既有注册（含内置），默认 false。</param>
    /// <returns>当前配置（链式调用）。</returns>
    /// <exception cref="ConditionExpressionException">符号为空、符号重复且未允许覆盖时抛出。</exception>
    public ConditionEngineOptions RegisterUnaryOperator(IConditionUnaryOperator unaryOperator, bool allowOverride = false)
    {
        ArgumentNullException.ThrowIfNull(unaryOperator);
        ValidateSymbol(unaryOperator.Symbol);
        EnsureUnarySymbolAvailable(unaryOperator.Symbol, unaryOperator, allowOverride);
        foreach (var alias in unaryOperator.Aliases)
        {
            EnsureUnarySymbolAvailable(alias, unaryOperator, allowOverride);
        }

        _unaryOperators.Add(unaryOperator);
        return this;
    }

    /// <summary>
    /// 注册自定义函数。
    /// </summary>
    /// <param name="function">函数实例（必须线程安全、无状态）。</param>
    /// <param name="allowOverride">是否允许覆盖同名函数的既有注册（含内置），默认 false。</param>
    /// <returns>当前配置（链式调用）。</returns>
    /// <exception cref="ConditionExpressionException">函数名为空、参数个数约束非法、名称重复且未允许覆盖时抛出。</exception>
    public ConditionEngineOptions RegisterFunction(IConditionFunction function, bool allowOverride = false)
    {
        if (string.IsNullOrWhiteSpace(function.Name))
        {
            throw RegistryError("函数名不能为空。");
        }

        if (function.MinArgumentCount < 0 || function.MaxArgumentCount < function.MinArgumentCount)
        {
            throw RegistryError($"函数 {function.Name} 的参数个数约束非法（Min={function.MinArgumentCount}, Max={function.MaxArgumentCount}）。");
        }

        var exists = _functions.Any(existing => existing.Name.Equals(function.Name, StringComparison.OrdinalIgnoreCase));
        if (exists && !allowOverride)
        {
            throw RegistryError($"函数 {function.Name} 已注册，覆盖需显式声明 allowOverride = true。");
        }

        if (exists)
        {
            _functions.RemoveAll(existing => existing.Name.Equals(function.Name, StringComparison.OrdinalIgnoreCase));
        }

        _functions.Add(function);
        return this;
    }

    /// <summary>
    /// 注册自定义字面量解析器（按注册顺序执行，先注册先命中）。
    /// </summary>
    /// <param name="literalParser">解析器实例（必须线程安全、无状态）。</param>
    /// <returns>当前配置（链式调用）。</returns>
    public ConditionEngineOptions RegisterLiteralParser(IConditionLiteralParser literalParser)
    {
        ArgumentNullException.ThrowIfNull(literalParser);
        _literalParsers.Add(literalParser);
        return this;
    }

    /// <summary>
    /// 注册自定义值强制转换器（自定义转换器优先于内置转换器执行）。
    /// </summary>
    /// <param name="valueCoercer">转换器实例（必须线程安全、无状态）。</param>
    /// <returns>当前配置（链式调用）。</returns>
    public ConditionEngineOptions RegisterValueCoercer(IConditionValueCoercer valueCoercer)
    {
        ArgumentNullException.ThrowIfNull(valueCoercer);
        _valueCoercers.Insert(0, valueCoercer);
        return this;
    }

    /// <summary>构建不可变注册表快照（引擎创建时调用一次）。</summary>
    internal ConditionRegistry Build()
        => new(_binaryOperators, _unaryOperators, _functions, _literalParsers, _valueCoercers);

    // ---- IConditionRegistry 显式接口实现（返回 void，链式调用走公开方法） ----

    void IConditionRegistry.RegisterBinaryOperator(IConditionBinaryOperator binaryOperator, bool allowOverride)
        => RegisterBinaryOperator(binaryOperator, allowOverride);

    void IConditionRegistry.RegisterUnaryOperator(IConditionUnaryOperator unaryOperator, bool allowOverride)
        => RegisterUnaryOperator(unaryOperator, allowOverride);

    void IConditionRegistry.RegisterFunction(IConditionFunction functionInstance, bool allowOverride)
        => RegisterFunction(functionInstance, allowOverride);

    void IConditionRegistry.RegisterLiteralParser(IConditionLiteralParser literalParser)
        => RegisterLiteralParser(literalParser);

    void IConditionRegistry.RegisterValueCoercer(IConditionValueCoercer valueCoercer)
        => RegisterValueCoercer(valueCoercer);

    private static void ValidateSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw RegistryError("运算符符号不能为空。");
        }
    }

    private static void ValidateAliases(IConditionBinaryOperator binaryOperator)
    {
        if (binaryOperator.Aliases.Any(alias => string.IsNullOrWhiteSpace(alias)))
        {
            throw RegistryError($"运算符 {binaryOperator.Symbol} 的别名不能为空白。");
        }
    }

    private void EnsureBinarySymbolAvailable(string symbol, IConditionBinaryOperator incoming, bool allowOverride)
    {
        var key = ConditionRegistry.NormalizeOperatorKey(symbol);
        var conflict = _binaryOperators.FirstOrDefault(existing =>
            ConditionRegistry.NormalizeOperatorKey(existing.Symbol) == key
            || existing.Aliases.Any(alias => ConditionRegistry.NormalizeOperatorKey(alias) == key));
        if (conflict is null || ReferenceEquals(conflict, incoming))
        {
            return;
        }

        if (!allowOverride)
        {
            throw RegistryError($"二元运算符符号 {symbol} 已被 {conflict.Symbol} 注册，覆盖需显式声明 allowOverride = true。");
        }

        _binaryOperators.Remove(conflict);
    }

    private void EnsureUnarySymbolAvailable(string symbol, IConditionUnaryOperator incoming, bool allowOverride)
    {
        var key = ConditionRegistry.NormalizeOperatorKey(symbol);
        var conflict = _unaryOperators.FirstOrDefault(existing =>
            ConditionRegistry.NormalizeOperatorKey(existing.Symbol) == key
            || existing.Aliases.Any(alias => ConditionRegistry.NormalizeOperatorKey(alias) == key));
        if (conflict is null || ReferenceEquals(conflict, incoming))
        {
            return;
        }

        if (!allowOverride)
        {
            throw RegistryError($"一元运算符符号 {symbol} 已被 {conflict.Symbol} 注册，覆盖需显式声明 allowOverride = true。");
        }

        _unaryOperators.Remove(conflict);
    }

    private static ConditionExpressionException RegistryError(string message)
        => new(ConditionExpressionErrorKind.Registry, message);
}
