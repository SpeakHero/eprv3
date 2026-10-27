/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 调用层全局配置项，按厂商名组织厂商字典
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.BuildingBlocks.Ai;

/// <summary>
/// AI 调用层全局配置项。对应配置节 <c>Qlx:Ai</c>。
/// 修改 <see cref="Strategy"/> 即可在固定 / 轮询两种调用模式间切换，业务代码零改动。
/// </summary>
public sealed record QlxAiOptions
{
    /// <summary>
    /// 配置节名。
    /// </summary>
    public const string SectionName = "Qlx:Ai";

    /// <summary>调用策略取值常量（配置值大小写不敏感）。</summary>
    public static class Strategies
    {
        /// <summary>固定模式：只调用 <see cref="FixedProvider"/> 指定的厂商，失败即抛出。</summary>
        public const string Fixed = "Fixed";

        /// <summary>轮询模式：按优先级逐个尝试，任一成功立即返回（故障转移）。</summary>
        public const string RoundRobin = "RoundRobin";
    }

    /// <summary>
    /// 是否启用 AI 调用层。默认 <see langword="false"/>：无密钥环境不误调用、不阻断启动。
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// 全局调用策略：<c>Fixed</c>（默认）或 <c>RoundRobin</c>（大小写不敏感）。
    /// </summary>
    [Required]
    public string Strategy { get; init; } = Strategies.Fixed;

    /// <summary>
    /// 固定模式下的目标厂商名（<see cref="Providers"/> 的键）；为空时取启用厂商中优先级最小者。
    /// </summary>
    public string? FixedProvider { get; init; }

    /// <summary>
    /// 默认单次调用超时（秒）；厂商未单独配置 <see cref="AiProviderOptions.TimeoutSeconds"/> 时生效。
    /// </summary>
    [Range(1, 600)]
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// 是否记录请求消息正文（默认 <see langword="false"/>，仅记录请求参数摘要）。
    /// </summary>
    public bool LogRequestContent { get; init; }

    /// <summary>
    /// 开启 <see cref="LogRequestContent"/> 时单条日志正文的最大截断长度（字符）。
    /// </summary>
    [Range(1, 4000)]
    public int MaxLogContentLength { get; init; } = 200;

    /// <summary>
    /// 厂商字典：键为厂商名（如 <c>zhipu</c>），值见 <see cref="AiProviderOptions"/>。
    /// 以厂商名为键可保证环境变量注入（<c>Qlx__Ai__Providers__zhipu__ApiKey</c>）不因数组下标漂移串号。
    /// </summary>
    public Dictionary<string, AiProviderOptions> Providers { get; init; } = [];
}
