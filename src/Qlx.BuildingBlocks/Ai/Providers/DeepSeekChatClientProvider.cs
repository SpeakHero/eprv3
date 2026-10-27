/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：DeepSeek 厂商工厂（默认地址 https://api.deepseek.com/v1/）
 */

using Qlx.Abstractions.Ai;

namespace Qlx.BuildingBlocks.Ai.Providers;

/// <summary>
/// DeepSeek 厂商工厂（OpenAI 兼容协议）。
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
/// <param name="httpClientFactory">命名 HttpClient 工厂。</param>
public sealed class DeepSeekChatClientProvider(IHttpClientFactory httpClientFactory)
    : OpenAiCompatibleProviderBase(httpClientFactory)
{
    /// <inheritdoc />
    public override string Type => AiProviderTypes.DeepSeek;

    /// <inheritdoc />
    public override string? DefaultBaseUrl => "https://api.deepseek.com/v1/";
}
