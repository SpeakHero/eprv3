/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 描述：条件表达式引擎工厂（默认引擎 / 回调配置 / 扩展门面三种创建方式）
 */

using Qlx.Abstractions.Expressions;

namespace Qlx.BuildingBlocks.Expressions;

/// <summary>
/// 条件表达式引擎工厂。以工厂模式解耦引擎创建与使用：
/// 各上层模块按需创建「内置能力 + 本模块扩展」的独立引擎实例，
/// 或直接使用共享的 <see cref="Default"/> 默认引擎（仅内置能力）。
/// </summary>
public static class ConditionExpressionEngineFactory
{
    /// <summary>默认引擎（仅内置能力，全局共享；无任何业务扩展注册）。</summary>
    public static IConditionExpressionEngine Default { get; } = CreateDefault();

    /// <summary>
    /// 创建默认引擎（仅内置能力）。
    /// </summary>
    /// <returns>引擎实例。</returns>
    public static IConditionExpressionEngine CreateDefault()
        => new ConditionExpressionEngine(new ConditionEngineOptions());

    /// <summary>
    /// 经配置回调创建引擎（适合单一扩展方简单注册场景）。
    /// </summary>
    /// <param name="configure">组合期配置回调。</param>
    /// <returns>引擎实例。</returns>
    public static IConditionExpressionEngine Create(Action<ConditionEngineOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new ConditionEngineOptions();
        configure(options);
        return new ConditionExpressionEngine(options);
    }

    /// <summary>
    /// 经扩展门面创建引擎（每个业务扩展实现 <see cref="IConditionExtension"/>，
    /// 工厂按声明顺序依次调用 Configure；冲突保护保证扩展之间互不覆盖除非显式声明）。
    /// </summary>
    /// <param name="extensions">业务扩展集合（可为空）。</param>
    /// <returns>引擎实例。</returns>
    public static IConditionExpressionEngine Create(params IConditionExtension[] extensions)
    {
        var options = new ConditionEngineOptions();
        foreach (var extension in extensions)
        {
            ArgumentNullException.ThrowIfNull(extension);
            extension.Configure(options);
        }

        return new ConditionExpressionEngine(options);
    }
}
