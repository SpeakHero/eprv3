/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：通用 OpenAI 兼容网关厂商工厂（无默认地址，必须配置 BaseUrl）
 */

using Qlx.Abstractions.Ai;

namespace Qlx.BuildingBlocks.Ai.Providers;

/// <summary>
/// 通用 OpenAI 兼容网关厂商工厂。无默认地址，配置必须显式填写 <c>BaseUrl</c>。
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
/// <param name="httpClientFactory">命名 HttpClient 工厂。</param>
public sealed class OpenAiCompatibleChatClientProvider(IHttpClientFactory httpClientFactory)
    : OpenAiCompatibleProviderBase(httpClientFactory)
{
    /// <inheritdoc />
    public override string Type => AiProviderTypes.OpenAiCompatible;

    /// <inheritdoc />
    public override string? DefaultBaseUrl => null;
}
