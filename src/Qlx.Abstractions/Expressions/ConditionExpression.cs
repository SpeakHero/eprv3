/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期三
 * 描述：已编译条件表达式制品（不可变 AST + 源文本 + 变量清单），支持 expr_json 双向互转
 */

using System.Text.Json;
using System.Text.Json.Serialization;
using Qlx.Abstractions.Serialization;

namespace Qlx.Abstractions.Expressions;

/// <summary>
/// 已编译条件表达式：解析产物，不可变、线程安全，可跨线程共享与缓存。
/// 引用方（流程定义、预警规则等）持久化本制品的 JSON 形态（对应审批中心 expr_json 字段），
/// 运行时经引擎反序列化后求值。
/// </summary>
public sealed record ConditionExpression
{
    /// <summary>表达式源文本（JSON 反序列化路径为空字符串）。</summary>
    public required string SourceText { get; init; }

    /// <summary>AST 根节点。</summary>
    public required ConditionNode Root { get; init; }

    /// <summary>表达式引用的全部变量名（去重、不区分大小写；供调用方预取数据）。</summary>
    public IReadOnlyList<string> Variables { get; init; } = [];

    /// <inheritdoc />
    public override string ToString() => SourceText;

    /// <summary>
    /// 序列化为 expr_json（AST 根节点的 JSON 形态，含 <c>$type</c> 判别符，走平台统一 JSON 选项；
    /// 源文本与变量清单不入载荷，反序列化时按 AST 重建）。
    /// </summary>
    /// <returns>expr_json 字符串。</returns>
    public string ToJson() => JsonSerializer.Serialize(Root, QlxJsonDefaults.Default);

    /// <summary>
    /// 从 expr_json 反序列化为表达式制品。
    /// </summary>
    /// <param name="json">expr_json 字符串。</param>
    /// <returns>表达式制品（运算符在求值时按符号经注册表解析）。</returns>
    /// <exception cref="ConditionExpressionException">JSON 结构非法时抛出。</exception>
    public static ConditionExpression FromJson(string json)
    {
        try
        {
            var root = JsonSerializer.Deserialize<ConditionNode>(json, QlxJsonDefaults.Default)
                ?? throw new ConditionExpressionException(
                    ConditionExpressionErrorKind.Parse,
                    "expr_json 反序列化结果为空。");
            return new ConditionExpression
            {
                SourceText = string.Empty,
                Root = root,
                Variables = CollectVariables(root)
            };
        }
        catch (ConditionExpressionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ConditionExpressionException(
                ConditionExpressionErrorKind.Parse,
                $"expr_json 反序列化失败：{ex.Message}",
                innerException: ex);
        }
    }

    /// <summary>
    /// 遍历 AST 收集全部变量名（去重、不区分大小写）。
    /// </summary>
    /// <param name="root">AST 根节点。</param>
    /// <returns>变量名清单。</returns>
    internal static IReadOnlyList<string> CollectVariables(ConditionNode root)
    {
        var collector = new CollectingVisitor();
        root.Accept(collector);
        return [.. collector.Names];
    }

    private sealed class CollectingVisitor : IConditionNodeVisitor
    {
        public HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Visit(LiteralNode node) { }
        public void Visit(VariableNode node) => Names.Add(node.Name);
        public void Visit(UnaryNode node) => node.Operand.Accept(this);

        public void Visit(BinaryNode node)
        {
            node.Left.Accept(this);
            node.Right.Accept(this);
        }

        public void Visit(FunctionNode node)
        {
            foreach (var argument in node.Arguments)
                argument.Accept(this);
        }

        public void Visit(ListNode node)
        {
            foreach (var item in node.Items)
                item.Accept(this);
        }

        public void Visit(TernaryNode node)
        {
            node.Condition.Accept(this);
            node.WhenTrue.Accept(this);
            node.WhenFalse.Accept(this);
        }
    }
}
