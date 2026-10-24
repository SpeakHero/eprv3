/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期三
 * 描述：自定义函数 / 字面量解析器 / 值强制转换器扩展契约
 */

namespace Qlx.Abstractions.Expressions;

/// <summary>
/// 自定义函数契约。业务方实现本接口并通过引擎工厂注册，
/// 表达式即可按 <c>函数名(参数...)</c> 语法调用。
/// </summary>
/// <remarks>
/// 实现要求：实例线程安全且无状态；函数名匹配不区分大小写。
/// 函数执行抛出的异常会被引擎包装为 <see cref="ConditionExpressionException"/>（保留内部异常）。
/// </remarks>
public interface IConditionFunction
{
    /// <summary>函数名（调用时不区分大小写，如 <c>Between</c>）。</summary>
    string Name { get; }

    /// <summary>最少参数个数（≥ 0）。</summary>
    int MinArgumentCount { get; }

    /// <summary>最多参数个数（≥ <see cref="MinArgumentCount"/>；不定长参数可设为 int.MaxValue）。</summary>
    int MaxArgumentCount { get; }

    /// <summary>
    /// 执行函数。
    /// </summary>
    /// <param name="arguments">实参值列表（已按表达式实参顺序求值）。</param>
    /// <param name="context">求值上下文（可访问 DI 服务）。</param>
    /// <returns>函数返回值。</returns>
    object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context);
}

/// <summary>
/// 字面量解析器契约：把词法层产生的 Number / String 词素转换为自定义类型的字面量值。
/// 引擎先尝试内置解析（整数 → long、小数 → decimal、科学计数 → double），
/// 失败后按注册顺序调用自定义解析器，首个返回 true 的生效。
/// </summary>
public interface IConditionLiteralParser
{
    /// <summary>
    /// 尝试把词素文本解析为字面量值。
    /// </summary>
    /// <param name="tokenKind">词素种类（Number / String）。</param>
    /// <param name="text">词素文本（String 已完成转义还原与去引号）。</param>
    /// <param name="value">解析结果。</param>
    /// <returns>true 表示解析成功；false 表示交由下一个解析器处理。</returns>
    bool TryParse(ConditionLiteralTokenKind tokenKind, string text, out object? value);
}

/// <summary>词素种类（字面量解析器视角）。</summary>
public enum ConditionLiteralTokenKind
{
    /// <summary>数字词素（整数 / 小数 / 科学计数）。</summary>
    Number,

    /// <summary>字符串词素（已去引号并还原转义）。</summary>
    Text
}

/// <summary>
/// 值强制转换器契约：在类型不匹配的比较场景中，把一侧值转换为目标类别后再比较。
/// 自定义转换器优先于内置转换器执行；转换失败返回 false 交由引擎内置逻辑兜底。
/// </summary>
public interface IConditionValueCoercer
{
    /// <summary>
    /// 尝试把 <paramref name="value"/> 转换为 <paramref name="target"/> 类别。
    /// </summary>
    /// <param name="value">原始值。</param>
    /// <param name="target">目标类别（取自另一侧操作数的类别）。</param>
    /// <param name="converted">转换结果。</param>
    /// <returns>true 表示转换成功。</returns>
    bool TryConvert(object? value, ConditionValueKind target, out object? converted);
}

/// <summary>
/// 条件值的类别（用于比较时的强制转换目标选择）。
/// </summary>
public enum ConditionValueKind
{
    /// <summary>数值（int / long / decimal / double 等可比较数值）。</summary>
    Numeric,

    /// <summary>布尔值。</summary>
    Boolean,

    /// <summary>字符串。</summary>
    Text,

    /// <summary>日期时间（DateTime / DateTimeOffset / DateOnly / TimeOnly）。</summary>
    DateTime,

    /// <summary>全局唯一标识（Guid）。</summary>
    UniqueIdentifier,

    /// <summary>其他引用类型（无自动转换，仅引用/值相等判断）。</summary>
    Other
}
