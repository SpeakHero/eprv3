/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期三
 * 描述：业务扩展门面接口（一次性配置自定义运算符 / 函数 / 字面量解析器 / 值转换器）
 */

namespace Qlx.Abstractions.Expressions;

/// <summary>
/// 条件表达式业务扩展门面：业务方（审批中心、各业务模板等）实现本接口，
/// 在引擎工厂创建回调中一次性完成全部自定义能力的注册，保持扩展配置内聚、可复用、可测试。
/// </summary>
/// <remarks>
/// 扩展实现必须满足：
/// <list type="number">
/// <item>Configure 仅在引擎组合期被调用一次，禁止缓存参数或延迟注册；</item>
/// <item>注册的运算符 / 函数 / 解析器实例必须线程安全且无状态；</item>
/// <item>与既有注册（含内置）冲突时须显式 allowOverride = true，否则注册失败快速暴露。</item>
/// </list>
/// </remarks>
public interface IConditionExtension
{
    /// <summary>
    /// 向引擎注册本扩展的全部自定义能力（组合期调用一次）。
    /// </summary>
    /// <param name="registry">引擎注册表（由引擎组装期注入）。</param>
    void Configure(IConditionRegistry registry);
}

/// <summary>
/// 条件表达式引擎注册表接口（业务扩展视角）。
/// 引擎实现本接口并在组合期注入给 <see cref="IConditionExtension.Configure"/>。
/// </summary>
public interface IConditionRegistry
{
    /// <summary>注册自定义二元运算符。</summary>
    /// <param name="binaryOperator">运算符实例（必须线程安全、无状态）。</param>
    /// <param name="allowOverride">是否允许覆盖同名符号的既有注册（含内置），默认 false。</param>
    void RegisterBinaryOperator(IConditionBinaryOperator binaryOperator, bool allowOverride = false);

    /// <summary>注册自定义一元（前缀）运算符。</summary>
    /// <param name="unaryOperator">运算符实例（必须线程安全、无状态）。</param>
    /// <param name="allowOverride">是否允许覆盖同名符号的既有注册（含内置），默认 false。</param>
    void RegisterUnaryOperator(IConditionUnaryOperator unaryOperator, bool allowOverride = false);

    /// <summary>注册自定义函数。</summary>
    /// <param name="functionInstance">函数实例（必须线程安全、无状态）。</param>
    /// <param name="allowOverride">是否允许覆盖同名函数的既有注册（含内置），默认 false。</param>
    void RegisterFunction(IConditionFunction functionInstance, bool allowOverride = false);

    /// <summary>注册自定义字面量解析器（按注册顺序执行，先注册先命中）。</summary>
    /// <param name="literalParser">解析器实例（必须线程安全、无状态）。</param>
    void RegisterLiteralParser(IConditionLiteralParser literalParser);

    /// <summary>注册自定义值强制转换器（自定义转换器优先于内置转换器执行）。</summary>
    /// <param name="valueCoercer">转换器实例（必须线程安全、无状态）。</param>
    void RegisterValueCoercer(IConditionValueCoercer valueCoercer);
}
