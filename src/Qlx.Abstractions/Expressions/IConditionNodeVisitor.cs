/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期三
 * 描述：条件节点访问者契约，供文本渲染 / 表达式校验 / SQL 翻译等横向扩展
 */

namespace Qlx.Abstractions.Expressions;

/// <summary>
/// 条件节点访问者契约：为 AST 横向能力（可读文本渲染、表达式校验、SQL 翻译等）提供标准扩展点。
/// 访问者以「父节点调用子节点 Accept」的方式递归遍历，实现方无需感知节点判别方式。
/// </summary>
public interface IConditionNodeVisitor
{
    /// <summary>访问字面量节点。</summary>
    /// <param name="node">节点。</param>
    void Visit(LiteralNode node);

    /// <summary>访问变量节点。</summary>
    /// <param name="node">节点。</param>
    void Visit(VariableNode node);

    /// <summary>访问一元运算节点。</summary>
    /// <param name="node">节点。</param>
    void Visit(UnaryNode node);

    /// <summary>访问二元运算节点。</summary>
    /// <param name="node">节点。</param>
    void Visit(BinaryNode node);

    /// <summary>访问函数调用节点。</summary>
    /// <param name="node">节点。</param>
    void Visit(FunctionNode node);

    /// <summary>访问列表字面量节点。</summary>
    /// <param name="node">节点。</param>
    void Visit(ListNode node);

    /// <summary>访问三元条件节点。</summary>
    /// <param name="node">节点。</param>
    void Visit(TernaryNode node);
}
