/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：单个 AI 厂商配置项（Type / BaseUrl / Model / ApiKey / 优先级 / 超时）
 */

using System.ComponentModel.DataAnnotations;

using Qlx.Abstractions.Ai;

namespace Qlx.BuildingBlocks.Ai;

/// <summary>
/// 单个 AI 厂商配置项。对应配置 <c>Qlx:Ai:Providers:{厂商名}</c>。
/// </summary>
public sealed record AiProviderOptions
{
    /// <summary>
    /// 厂商类型标识（见 <see cref="AiProviderTypes"/>），决定使用哪个协议适配器（大小写不敏感）。
    /// </summary>
    [Required]
    public string Type { get; init; } = AiProviderTypes.OpenAiCompatible;

    /// <summary>
    /// 厂商接口基地址；为空时使用该类型适配器的默认地址。
    /// </summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// 默认模型名；请求未指定 <see cref="AiChatRequest.Model"/> 时使用。
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// 访问密钥。禁止明文写入 <c>config/*.json</c>，须经环境变量或密钥引用注入。
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// 是否启用该厂商（默认启用）。
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// 调用优先级（数值越小越优先，默认 100）。
    /// </summary>
    [Range(0, int.MaxValue)]
    public int Priority { get; init; } = 100;

    /// <summary>
    /// 该厂商单独的超时（秒）；为空时使用全局 <see cref="QlxAiOptions.TimeoutSeconds"/>。
    /// </summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>
    /// 返回掩码 <see cref="ApiKey"/> 的配置摘要，避免密钥经日志或异常外泄。
    /// </summary>
    /// <returns>配置摘要文本。</returns>
    public override string ToString()
        => $"{{ Type = {Type}, BaseUrl = {BaseUrl}, Model = {Model}, Enabled = {Enabled}, Priority = {Priority}, TimeoutSeconds = {TimeoutSeconds}, ApiKey = {(string.IsNullOrWhiteSpace(ApiKey) ? "(未配置)" : "(已配置)")} }}";
}
