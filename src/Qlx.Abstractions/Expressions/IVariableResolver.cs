/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期三
 * 描述：变量解析契约（接口），具体实现（字典 / 组合）保留在 BuildingBlocks
 */

namespace Qlx.Abstractions.Expressions;

/// <summary>
/// 变量解析契约：引擎求值 <c>变量</c> / <c>${变量}</c> 节点时经此取值。
/// 业务方（审批中心、业务模板等）按自身字段元数据实现本接口并传入求值调用。
/// </summary>
public interface IVariableResolver
{
    /// <summary>
    /// 尝试解析变量值。
    /// </summary>
    /// <param name="name">变量名（去掉 <c>${}</c> 包装后的名称）。</param>
    /// <param name="value">解析结果（允许为 null，表示变量存在且值为空）。</param>
    /// <returns>true 表示变量存在；false 表示无法解析。</returns>
    bool TryResolve(string name, out object? value);
}
