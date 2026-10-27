/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 描述：端点授权规则映射提供者契约——各模块以「配置类基线」形式内聚声明本模块
 *       控制器/动作与授权策略的绑定关系（与模块 Manifest 权限点声明同构），
 *       由 BuildingBlocks 的 EndpointPolicyCatalog 在启动期经 DI 多注册合并。
 */

namespace Qlx.Abstractions.Authorization;

/// <summary>
/// 端点授权规则映射提供者。模块可选实现并以
/// <c>services.AddSingleton&lt;IEndpointPolicyMapProvider, XxxEndpointPolicyMap&gt;()</c> 注册；
/// 多实现由 DI 原生集合解析合并，新增模块接入零接线。
/// </summary>
public interface IEndpointPolicyMapProvider
{
    /// <summary>获取本模块声明的端点授权规则基线（内容 = 原控制器标注的 1:1 快照）。</summary>
    /// <returns>规则列表（允许为空集合）。</returns>
    IReadOnlyList<EndpointPolicyRule> GetRules();
}
