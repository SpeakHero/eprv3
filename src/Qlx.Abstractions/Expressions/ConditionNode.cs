/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 16:15:00 +08:00 中国标准时间 星期三
 * 描述：条件表达式 AST 节点判别联合（abstract record + 子 record），从 BuildingBlocks 下沉，
 *      Evaluate 依赖 IConditionEvaluationContext 接口契约（BuildingBlocks 实现）
 */

using System.Text.Json.Serialization;

namespace Qlx.Abstractions.Expressions;

/// <summary>
/// 条件表达式 AST 节点抽象基类（判别联合）。
/// 全部节点形态以子 record 承载，由多态求值与访问者分发处理，引擎核心禁止以 switch 区分节点形态。
/// </summary>
/// <remarks>
/// 节点树由引擎解析产生后即不可变、线程安全，可在多线程间自由共享。
/// 序列化：经 <see cref="ConditionExpression.ToJson"/> 输出为 expr_json（含 <c>$type</c> 判别符），
/// 支持审批中心 condition-builder 产物与引擎互导。
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(LiteralNode), "literal")]
[JsonDerivedType(typeof(VariableNode), "variable")]
[JsonDerivedType(typeof(UnaryNode), "unary")]
[JsonDerivedType(typeof(BinaryNode), "binary")]
[JsonDerivedType(typeof(FunctionNode), "function")]
[JsonDerivedType(typeof(ListNode), "list")]
[JsonDerivedType(typeof(TernaryNode), "ternary")]
public abstract record ConditionNode
{
    /// <summary>
    /// 接受访问者（多态分发，用于文本渲染、校验等横向能力扩展）。
    /// </summary>
    /// <param name="visitor">节点访问者。</param>
    public abstract void Accept(IConditionNodeVisitor visitor);

    /// <summary>多态求值（引擎实现内部调用）。</summary>
    /// <param name="context">求值上下文。</param>
    /// <returns>节点求值结果。</returns>
    public abstract object? Evaluate(IConditionEvaluationContext context);
}

/// <summary>字面量节点：数字 / 字符串 / 布尔 / null 常量。</summary>
public sealed record LiteralNode([property: JsonConverter(typeof(ConditionValueJsonConverter))] object? Value) : ConditionNode
{
    /// <inheritdoc />
    public override void Accept(IConditionNodeVisitor visitor) => visitor.Visit(this);

    /// <inheritdoc />
    public override object? Evaluate(IConditionEvaluationContext context) => Value;
}

/// <summary>变量节点：裸标识符变量（如 <c>total_amount</c>）或参数变量（如 <c>${total_amount}</c>）。</summary>
public sealed record VariableNode(string Name) : ConditionNode
{
    /// <inheritdoc />
    public override void Accept(IConditionNodeVisitor visitor) => visitor.Visit(this);

    /// <inheritdoc />
    public override object? Evaluate(IConditionEvaluationContext context) => context.ResolveVariable(Name);
}

/// <summary>一元运算节点：前缀运算符 + 操作数（如 <c>!approved</c>、<c>-amount</c>）。</summary>
public sealed record UnaryNode(string OperatorSymbol, ConditionNode Operand) : ConditionNode
{
    /// <summary>解析期已绑定的运算符实例（JSON 反序列化路径为 null，求值时按符号经注册表解析）。</summary>
    [JsonIgnore]
    public IConditionUnaryOperator? ResolvedOperator { get; init; }

    /// <inheritdoc />
    public override void Accept(IConditionNodeVisitor visitor) => visitor.Visit(this);

    /// <inheritdoc />
    public override object? Evaluate(IConditionEvaluationContext context)
    {
        var operand = Operand.Evaluate(context);
        var op = ResolvedOperator ?? context.RequireUnaryOperator(OperatorSymbol);
        return ConditionNodeGuard.Invoke(() => op.Apply(operand, context), $"一元运算符 {OperatorSymbol}");
    }
}

/// <summary>二元运算节点：左操作数 + 运算符 + 右操作数（逻辑 / 比较 / 算术 / 成员判断等）。</summary>
public sealed record BinaryNode(ConditionNode Left, string OperatorSymbol, ConditionNode Right) : ConditionNode
{
    /// <summary>解析期已绑定的运算符实例（JSON 反序列化路径为 null，求值时按符号经注册表解析）。</summary>
    [JsonIgnore]
    public IConditionBinaryOperator? ResolvedOperator { get; init; }

    /// <inheritdoc />
    public override void Accept(IConditionNodeVisitor visitor) => visitor.Visit(this);

    /// <inheritdoc />
    public override object? Evaluate(IConditionEvaluationContext context)
    {
        var op = ResolvedOperator ?? context.RequireBinaryOperator(OperatorSymbol);
        var left = Left.Evaluate(context);
        if (op is IConditionShortCircuitOperator shortCircuit && shortCircuit.TryShortCircuit(left, context, out var shortCircuitResult))
        {
            return shortCircuitResult;
        }

        var right = Right.Evaluate(context);
        return ConditionNodeGuard.Invoke(() => op.Apply(left, right, context), $"二元运算符 {OperatorSymbol}");
    }
}

/// <summary>函数调用节点：函数名 + 实参节点列表（如 <c>between(amount, 1000, 5000)</c>）。</summary>
public sealed record FunctionNode(string Name, IReadOnlyList<ConditionNode> Arguments) : ConditionNode
{
    /// <inheritdoc />
    public override void Accept(IConditionNodeVisitor visitor) => visitor.Visit(this);

    /// <inheritdoc />
    public override object? Evaluate(IConditionEvaluationContext context)
    {
        var function = context.RequireFunction(Name);
        if (Arguments.Count < function.MinArgumentCount || Arguments.Count > function.MaxArgumentCount)
        {
            throw new ConditionExpressionException(
                ConditionExpressionErrorKind.Evaluate,
                $"函数 {Name} 参数个数不合法：期望 {function.MinArgumentCount}~{function.MaxArgumentCount} 个，实际 {Arguments.Count} 个。",
                expressionSnippet: Name);
        }

        var arguments = new object?[Arguments.Count];
        for (var i = 0; i < Arguments.Count; i++)
        {
            arguments[i] = Arguments[i].Evaluate(context);
        }

        return ConditionNodeGuard.Invoke(() => function.Invoke(arguments, context), $"函数 {Name}");
    }
}

/// <summary>列表字面量节点：IN / NOT IN 的右操作数（如 <c>["PURCHASE", "EXPENSE"]</c>）。</summary>
public sealed record ListNode(IReadOnlyList<ConditionNode> Items) : ConditionNode
{
    /// <inheritdoc />
    public override void Accept(IConditionNodeVisitor visitor) => visitor.Visit(this);

    /// <inheritdoc />
    public override object? Evaluate(IConditionEvaluationContext context)
    {
        var items = new object?[Items.Count];
        for (var i = 0; i < Items.Count; i++)
        {
            items[i] = Items[i].Evaluate(context);
        }

        return items;
    }
}

/// <summary>三元条件节点：<c>条件 ? 真值 : 假值</c>（用于业务模板取值场景）。</summary>
public sealed record TernaryNode(ConditionNode Condition, ConditionNode WhenTrue, ConditionNode WhenFalse) : ConditionNode
{
    /// <inheritdoc />
    public override void Accept(IConditionNodeVisitor visitor) => visitor.Visit(this);

    /// <inheritdoc />
    public override object? Evaluate(IConditionEvaluationContext context)
        => context.IsTruthy(Condition.Evaluate(context))
            ? WhenTrue.Evaluate(context)
            : WhenFalse.Evaluate(context);
}

/// <summary>
/// 业务扩展代码（自定义运算符 / 函数）调用防护：
/// <list type="bullet">
/// <item>引擎自身异常（<see cref="ConditionExpressionException"/>）原样上抛；</item>
/// <item>取消异常（OperationCanceledException）原样上抛（禁止吞取消）；</item>
/// <item>其余扩展代码异常统一包装为求值异常并保留内部异常，保证上层业务只需捕获引擎异常。</item>
/// </list>
/// </summary>
internal static class ConditionNodeGuard
{
    internal static object? Invoke(Func<object?> action, string extensionDescription)
    {
        try
        {
            return action();
        }
        catch (ConditionExpressionException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ConditionExpressionException(
                ConditionExpressionErrorKind.Evaluate,
                $"{extensionDescription} 执行失败：{ex.Message}",
                expressionSnippet: extensionDescription,
                innerException: ex);
        }
    }
}
