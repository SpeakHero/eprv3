/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 描述：条件表达式 AST → 可读文本渲染器（优先级感知的最小括号渲染），供 expr_text 生成与调试
 */

using System.Globalization;
using System.Text;

using Qlx.Abstractions.Expressions;

namespace Qlx.BuildingBlocks.Expressions;

/// <summary>
/// 条件表达式可读文本渲染器。按运算符优先级生成最小括号的规范化表达式文本，
/// 对应审批中心 expr_text 字段（组件自动生成可读文本）的实现基座。
/// </summary>
/// <remarks>
/// 对未经引擎解析绑定的节点树（如 JSON 反序列化产物），运算符优先级未知时采用保守加括号策略，保证语义等价。
/// 渲染器无状态、线程安全。
/// </remarks>
public sealed class ConditionNodeTextRenderer : IConditionNodeVisitor
{
    private readonly StringBuilder _builder = new();
    private int _minPrecedence;
    private bool _isRightOperand;

    private ConditionNodeTextRenderer()
    {
    }

    /// <summary>
    /// 渲染节点树为可读表达式文本。
    /// </summary>
    /// <param name="node">AST 根节点。</param>
    /// <returns>规范化表达式文本。</returns>
    public static string Render(ConditionNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var renderer = new ConditionNodeTextRenderer();
        // 根节点不包外层括号（含 JSON 反序列化路径的未知优先级根节点）；其嵌套子节点仍按保守策略加括号
        renderer._minPrecedence = 0;
        renderer._isRightOperand = false;
        node.Accept(renderer);
        return renderer._builder.ToString();
    }

    /// <inheritdoc />
    public void Visit(LiteralNode node) => _builder.Append(FormatLiteral(node.Value));

    /// <inheritdoc />
    public void Visit(VariableNode node) => _builder.Append(node.Name);

    /// <inheritdoc />
    public void Visit(UnaryNode node)
    {
        _builder.Append(node.OperatorSymbol);
        // 一元运算符绑定最紧：仅当操作数是复合节点（二元 / 三元）时加括号
        RenderChild(node.Operand, minPrecedence: int.MaxValue, isRightOperand: false);
    }

    /// <inheritdoc />
    public void Visit(BinaryNode node)
    {
        // 自身优先级：已绑定运算符时取其优先级；未绑定（JSON 路径）时按最低优先级处理，
        // 使子节点全部加括号，保证渲染文本语义与节点树等价
        var selfPrecedence = node.ResolvedOperator is { } op ? (int)op.Precedence : 0;
        var rightAssociative = node.ResolvedOperator?.IsRightAssociative ?? false;

        // 左结合：左子同级不加括号、右子同级加括号；右结合相反
        RenderChild(node.Left, selfPrecedence, isRightOperand: rightAssociative);
        _builder.Append(' ').Append(node.OperatorSymbol).Append(' ');
        RenderChild(node.Right, selfPrecedence, isRightOperand: !rightAssociative);
    }

    /// <inheritdoc />
    public void Visit(FunctionNode node)
    {
        _builder.Append(node.Name).Append('(');
        for (var i = 0; i < node.Arguments.Count; i++)
        {
            if (i > 0)
            {
                _builder.Append(", ");
            }

            RenderChild(node.Arguments[i], minPrecedence: 0, isRightOperand: false);
        }

        _builder.Append(')');
    }

    /// <inheritdoc />
    public void Visit(ListNode node)
    {
        _builder.Append('[');
        for (var i = 0; i < node.Items.Count; i++)
        {
            if (i > 0)
            {
                _builder.Append(", ");
            }

            RenderChild(node.Items[i], minPrecedence: 0, isRightOperand: false);
        }

        _builder.Append(']');
    }

    /// <inheritdoc />
    public void Visit(TernaryNode node)
    {
        RenderChild(node.Condition, minPrecedence: 0, isRightOperand: false);
        _builder.Append(" ? ");
        RenderChild(node.WhenTrue, minPrecedence: 0, isRightOperand: false);
        _builder.Append(" : ");
        RenderChild(node.WhenFalse, minPrecedence: 0, isRightOperand: false);
    }

    private void RenderChild(ConditionNode child, int minPrecedence, bool isRightOperand)
    {
        var savedPrecedence = _minPrecedence;
        var savedIsRight = _isRightOperand;

        var childPrecedence = child switch
        {
            BinaryNode { ResolvedOperator: { } op } => (int)op.Precedence,
            BinaryNode => -1,
            TernaryNode => (int)ConditionPrecedence.LogicalOr - 1,
            _ => int.MaxValue
        };

        // 复合子节点优先级不足（或同级且处于右操作数位）时加括号；未知优先级仅在嵌套场景保守加括号
        var needsParens = (childPrecedence < minPrecedence || (childPrecedence == minPrecedence && isRightOperand))
            && child is BinaryNode or TernaryNode;

        if (needsParens)
        {
            _builder.Append('(');
            _minPrecedence = 0;
            _isRightOperand = false;
            child.Accept(this);
            _builder.Append(')');
        }
        else
        {
            _minPrecedence = minPrecedence;
            _isRightOperand = isRightOperand;
            child.Accept(this);
        }

        _minPrecedence = savedPrecedence;
        _isRightOperand = savedIsRight;
    }

    private static string FormatLiteral(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        string s => "'" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'",
        DateTime dt => "'" + dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "'",
        DateOnly d => "'" + d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "'",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null"
    };
}
