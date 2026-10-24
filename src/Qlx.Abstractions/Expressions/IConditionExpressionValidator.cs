/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期三
 * 描述：条件表达式校验契约（语法 / 深度 / 字段存在性 / 操作符类型匹配）
 */

namespace Qlx.Abstractions.Expressions;

/// <summary>
/// 条件表达式校验契约（语法 / 深度 / 字段存在性 / 操作符类型匹配）。
/// </summary>
public interface IConditionExpressionValidator
{
    /// <summary>校验表达式源文本。</summary>
    /// <param name="expressionText">表达式源文本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>校验结果。</returns>
    Task<ConditionValidationResult> ValidateAsync(string expressionText, CancellationToken cancellationToken);
}

/// <summary>
/// 条件表达式校验结果。
/// </summary>
/// <param name="IsValid">校验是否通过。</param>
/// <param name="Errors">错误列表（阻断性问题）。</param>
/// <param name="Warnings">警告列表（非阻断性问题）。</param>
public sealed record ConditionValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
