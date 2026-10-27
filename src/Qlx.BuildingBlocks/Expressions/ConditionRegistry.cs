/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 描述：条件注册表不可变快照（运算符 / 函数 / 字面量解析器 / 值转换器的线程安全查找表）
 */

using System.Collections.Frozen;

using Qlx.Abstractions.Expressions;

namespace Qlx.BuildingBlocks.Expressions;

/// <summary>
/// 条件注册表：引擎实例创建时由 <see cref="ConditionEngineOptions"/> 一次性构建的不可变快照。
/// 注册表实例不可变且线程安全，业务扩展在组合期注册，运行期零锁查找，
/// 保证扩展代码对引擎核心无运行时副作用（向后兼容由 Options 覆盖保护实现）。
/// </summary>
internal sealed class ConditionRegistry
{
    private readonly FrozenDictionary<string, IConditionBinaryOperator> _binaryOperators;
    private readonly FrozenDictionary<string, IConditionUnaryOperator> _unaryOperators;
    private readonly FrozenDictionary<string, IConditionFunction> _functions;

    /// <summary>二元运算符注册顺序（用于诊断）。</summary>
    internal IReadOnlyList<IConditionBinaryOperator> BinaryOperatorList { get; }

    /// <summary>一元运算符注册顺序（用于诊断）。</summary>
    internal IReadOnlyList<IConditionUnaryOperator> UnaryOperatorList { get; }

    /// <summary>字面量解析器链（注册顺序执行，首个命中生效）。</summary>
    internal IReadOnlyList<IConditionLiteralParser> LiteralParsers { get; }

    /// <summary>值强制转换器链（自定义在前、内置兜底）。</summary>
    internal IReadOnlyList<IConditionValueCoercer> ValueCoercers { get; }

    /// <summary>符号类运算符全集（非纯字母符号，供词法器做最长匹配切分）。</summary>
    internal IReadOnlyCollection<string> SymbolicOperatorTexts { get; }

    public ConditionRegistry(
        IEnumerable<IConditionBinaryOperator> binaryOperators,
        IEnumerable<IConditionUnaryOperator> unaryOperators,
        IEnumerable<IConditionFunction> functions,
        IEnumerable<IConditionLiteralParser> literalParsers,
        IEnumerable<IConditionValueCoercer> valueCoercers)
    {
        var binaryList = binaryOperators.ToArray();
        var unaryList = unaryOperators.ToArray();
        var functionList = functions.ToArray();

        var binaryMap = new Dictionary<string, IConditionBinaryOperator>(StringComparer.Ordinal);
        foreach (var op in binaryList)
        {
            binaryMap[NormalizeOperatorKey(op.Symbol)] = op;
            foreach (var alias in op.Aliases)
            {
                binaryMap[NormalizeOperatorKey(alias)] = op;
            }
        }

        var unaryMap = new Dictionary<string, IConditionUnaryOperator>(StringComparer.Ordinal);
        foreach (var op in unaryList)
        {
            unaryMap[NormalizeOperatorKey(op.Symbol)] = op;
            foreach (var alias in op.Aliases)
            {
                unaryMap[NormalizeOperatorKey(alias)] = op;
            }
        }

        var functionMap = new Dictionary<string, IConditionFunction>(StringComparer.OrdinalIgnoreCase);
        foreach (var function in functionList)
        {
            functionMap[function.Name] = function;
        }

        _binaryOperators = binaryMap.ToFrozenDictionary(StringComparer.Ordinal);
        _unaryOperators = unaryMap.ToFrozenDictionary(StringComparer.Ordinal);
        _functions = functionMap.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        BinaryOperatorList = binaryList;
        UnaryOperatorList = unaryList;
        LiteralParsers = [.. literalParsers];
        ValueCoercers = [.. valueCoercers];
        SymbolicOperatorTexts = [.. binaryMap.Keys.Union(unaryMap.Keys).Where(k => !IsWord(k))];
    }

    internal IConditionBinaryOperator? TryGetBinaryOperator(string symbol)
        => _binaryOperators.TryGetValue(NormalizeOperatorKey(symbol), out var op) ? op : null;

    internal IConditionUnaryOperator? TryGetUnaryOperator(string symbol)
        => _unaryOperators.TryGetValue(NormalizeOperatorKey(symbol), out var op) ? op : null;

    internal IConditionFunction? TryGetFunction(string name)
        => _functions.TryGetValue(name, out var function) ? function : null;

    // 纯字母符号（AND / OR / NOT / IN 等词运算符）按不区分大小写归一；符号类运算符区分大小写
    internal static string NormalizeOperatorKey(string symbol)
        => IsWord(symbol) ? symbol.ToUpperInvariant() : symbol;

    private static bool IsWord(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var ch in text)
        {
            if (!char.IsLetter(ch))
            {
                return false;
            }
        }

        return true;
    }
}
