/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 描述：集中式授权端点策略配置项——绑定 config/common/authorization.json 的
 *       endpointPolicies 节（JSON 覆盖层），启动期读取一次（IOptions，非热更）。
 */

using Qlx.Abstractions.Authorization;

namespace Qlx.BuildingBlocks.Authorization;

/// <summary>
/// 端点授权策略覆盖配置。
/// <para>
/// <see cref="Rules"/> 按 (Controller, Action) 键同键覆盖模块 MapProvider 基线
/// （基线新增键不受存量 JSON 掩盖）；<see cref="DisabledKeys"/> 按
/// <c>"Controller"</c> / <c>"Controller.Action"</c> 键删除对应规则，
/// 被删端点回退「仅认证」兜底（安全降级方向固定，永不降级为匿名放行）。
/// </para>
/// </summary>
public sealed class EndpointPolicyOptions
{
    /// <summary>配置节名（<c>endpointPolicies</c>）。</summary>
    public const string SectionName = "endpointPolicies";

    /// <summary>JSON 覆盖规则集。</summary>
    public List<EndpointPolicyRule> Rules { get; set; } = [];

    /// <summary>禁用键集（<c>"Controller"</c> 或 <c>"Controller.Action"</c>，序数不区分大小写）。</summary>
    public List<string> DisabledKeys { get; set; } = [];
}
