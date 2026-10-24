/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期三
 * 描述：条件表达式引擎统一业务异常（解析 / 求值 / 注册冲突），携带错误位置与表达式片段
 */

using Qlx.Abstractions.Exceptions;

namespace Qlx.Abstractions.Expressions;

/// <summary>
/// 条件表达式引擎异常类别。
/// </summary>
public enum ConditionExpressionErrorKind
{
    /// <summary>表达式语法解析失败（非法字符、括号不配对、运算符缺操作数等）。</summary>
    Parse,

    /// <summary>表达式求值失败（变量未解析、除零、类型不兼容、函数执行异常等）。</summary>
    Evaluate,

    /// <summary>扩展注册冲突或非法（符号重复、参数约束非法等）。</summary>
    Registry
}

/// <summary>
/// 条件表达式引擎统一业务异常。解析、求值、扩展注册过程中的一切引擎级错误均以此类型抛出，
/// 上层业务只需捕获本异常即可，无需感知引擎内部实现细节。
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
/// <param name="kind">错误类别。</param>
/// <param name="message">可读错误消息。</param>
/// <param name="position">错误位置（可选）。</param>
/// <param name="expressionSnippet">表达式片段（可选）。</param>
/// <param name="innerException">内部异常（可选，用于包装扩展函数抛出的原始异常）。</param>
public sealed class ConditionExpressionException(
    ConditionExpressionErrorKind kind,
    string message,
    int? position = null,
    string? expressionSnippet = null,
    Exception? innerException = null) : BusinessException(ResolveErrorCode(kind), message, null, innerException)
{
    /// <summary>错误类别。</summary>
    public ConditionExpressionErrorKind Kind { get; } = kind;

    /// <summary>错误发生位置（源表达式中的字符偏移，从 0 开始；与位置无关的错误为 null）。</summary>
    public int? Position { get; } = position;

    /// <summary>发生错误的源表达式文本（截断至错误位置附近的片段，便于定位）。</summary>
    public string? ExpressionSnippet { get; } = expressionSnippet;

    /// <summary>解析错误统一错误码。</summary>
    public const string ParseErrorCode = "CONDITION_EXPR_PARSE_ERROR";

    /// <summary>求值错误统一错误码。</summary>
    public const string EvaluateErrorCode = "CONDITION_EXPR_EVAL_ERROR";

    /// <summary>注册冲突统一错误码。</summary>
    public const string RegistryErrorCode = "CONDITION_EXPR_REGISTRY_ERROR";

    private static string ResolveErrorCode(ConditionExpressionErrorKind kind) => kind switch
    {
        ConditionExpressionErrorKind.Parse => ParseErrorCode,
        ConditionExpressionErrorKind.Evaluate => EvaluateErrorCode,
        _ => RegistryErrorCode
    };
}
