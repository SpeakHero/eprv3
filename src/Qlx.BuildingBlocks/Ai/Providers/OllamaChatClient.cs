/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：Ollama 本地开源模型适配器（/api/chat，免密钥）
 */

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Qlx.Abstractions.Ai;
using Qlx.Abstractions.Serialization;

namespace Qlx.BuildingBlocks.Ai.Providers;

/// <summary>
/// Ollama 本地开源模型适配器。向 <c>{BaseUrl}/api/chat</c> 发起 <c>POST</c>，
/// 固定 <c>stream=false</c>，要求 JSON 时传 <c>format=json</c>，解析 <c>message.content</c> 与
/// <c>prompt_eval_count</c> / <c>eval_count</c> 用量。
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
/// <param name="providerName">厂商名（<c>Qlx:Ai:Providers</c> 的键）。</param>
/// <param name="provider">该厂商的配置项（Ollama 不要求 <c>ApiKey</c>）。</param>
/// <param name="httpClientFactory">命名 HttpClient 工厂（阶段 D 按 <see cref="AiHttpClientNames.For"/> 注册）。</param>
/// <param name="defaultBaseUrl">该厂商类型的默认接口基地址（可为空，为空时必须显式配置 <c>BaseUrl</c>）。</param>
public sealed class OllamaChatClient(
    string providerName,
    AiProviderOptions provider,
    IHttpClientFactory httpClientFactory,
    string? defaultBaseUrl) : IAiChatClient
{
    private const int MaxErrorSnippetLength = 500;

    /// <inheritdoc />
    public string ProviderName => providerName;

    /// <inheritdoc />
    /// <exception cref="AiProviderException">厂商配置缺失、HTTP 调用失败或响应无效时抛出。</exception>
    public async Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string baseUrl = ResolveBaseUrl();
        string model = ResolveModel(request.Model);

        HttpClient httpClient = httpClientFactory.CreateClient(AiHttpClientNames.For(providerName));
        string requestJson = JsonSerializer.Serialize(BuildPayload(request, model), QlxJsonDefaults.Default);

        using HttpRequestMessage httpRequest = new(HttpMethod.Post, string.Concat(baseUrl.TrimEnd('/'), "/api/chat"))
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json"),
        };

        // Ollama 默认免密钥；若部署侧配置了反向代理鉴权，则按 Bearer 透传
        string? apiKey = provider.ApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        long startTimestamp = Stopwatch.GetTimestamp();
        using HttpResponseMessage response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            int statusCode = (int)response.StatusCode;
            throw new AiProviderException(
                providerName,
                AiErrorCodes.HttpStatus(statusCode),
                $"HTTP {statusCode}: {Truncate(errorBody, MaxErrorSnippetLength)}",
                statusCode);
        }

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        (string? content, int? promptTokens, int? completionTokens, string? finishReason) = ParseResponse(responseBody);

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new AiProviderException(providerName, AiErrorCodes.InvalidResponse, "响应缺少 message.content");
        }

        long durationMilliseconds = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        return new AiChatResponse(
            content,
            providerName,
            model,
            durationMilliseconds,
            promptTokens,
            completionTokens,
            finishReason);
    }

    private string ResolveBaseUrl()
    {
        string? baseUrl = provider.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = defaultBaseUrl;
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new AiProviderException(
                providerName,
                AiErrorCodes.ProviderNotConfigured,
                $"AI 厂商 {providerName} 的 BaseUrl 未配置。");
        }

        return baseUrl;
    }

    private string ResolveModel(string? requestModel)
    {
        string? model = requestModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            model = provider.Model;
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new AiProviderException(
                providerName,
                AiErrorCodes.ProviderNotConfigured,
                $"AI 厂商 {providerName} 的 Model 未配置。");
        }

        return model;
    }

    private static OllamaChatRequestPayload BuildPayload(AiChatRequest request, string model)
    {
        OllamaMessagePayload[] messages = new OllamaMessagePayload[request.Messages.Count];
        for (int index = 0; index < request.Messages.Count; index++)
        {
            AiMessage message = request.Messages[index];
            messages[index] = new OllamaMessagePayload
            {
                Role = ToRoleName(message.Role),
                Content = message.Content,
            };
        }

        OllamaChatOptionsPayload? options = null;
        if (request.Temperature is not null || request.MaxTokens is not null)
        {
            options = new OllamaChatOptionsPayload
            {
                Temperature = request.Temperature,
                NumPredict = request.MaxTokens,
            };
        }

        return new OllamaChatRequestPayload
        {
            Model = model,
            Messages = messages,
            Options = options,
            Format = request.ResponseFormat == AiResponseFormat.Json ? "json" : null,
        };
    }

    private static (string? Content, int? PromptTokens, int? CompletionTokens, string? FinishReason) ParseResponse(
        string responseBody)
    {
        string? content = null;
        int? promptTokens = null;
        int? completionTokens = null;
        string? finishReason = null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (content, promptTokens, completionTokens, finishReason);
            }

            if (root.TryGetProperty("message", out JsonElement messageElement)
                && messageElement.ValueKind == JsonValueKind.Object
                && messageElement.TryGetProperty("content", out JsonElement contentElement)
                && contentElement.ValueKind == JsonValueKind.String)
            {
                content = contentElement.GetString();
            }

            if (root.TryGetProperty("done_reason", out JsonElement doneReasonElement)
                && doneReasonElement.ValueKind == JsonValueKind.String)
            {
                finishReason = doneReasonElement.GetString();
            }

            if (root.TryGetProperty("prompt_eval_count", out JsonElement promptElement)
                && promptElement.TryGetInt32(out int promptValue))
            {
                promptTokens = promptValue;
            }

            if (root.TryGetProperty("eval_count", out JsonElement completionElement)
                && completionElement.TryGetInt32(out int completionValue))
            {
                completionTokens = completionValue;
            }
        }
        catch (JsonException)
        {
            // 响应体不是合法 JSON：保持 content 为空，交由调用方按无效响应处理
        }

        return (content, promptTokens, completionTokens, finishReason);
    }

    private static string ToRoleName(AiMessageRole role) => role switch
    {
        AiMessageRole.System => "system",
        AiMessageRole.User => "user",
        AiMessageRole.Assistant => "assistant",
        _ => throw new ArgumentException($"未知的 AI 消息角色：{role}。", nameof(role)),
    };

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private sealed record OllamaChatRequestPayload
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("messages")]
        public required IReadOnlyList<OllamaMessagePayload> Messages { get; init; }

        /// <summary>是否流式返回；本层统一 <see langword="false"/>。XML 文档仅用于说明字段语义。</summary>
        [JsonPropertyName("stream")]
        public bool Stream { get; init; }

        [JsonPropertyName("format")]
        public string? Format { get; init; }

        [JsonPropertyName("options")]
        public OllamaChatOptionsPayload? Options { get; init; }
    }

    private sealed record OllamaMessagePayload
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }
    }

    private sealed record OllamaChatOptionsPayload
    {
        [JsonPropertyName("temperature")]
        public double? Temperature { get; init; }

        [JsonPropertyName("num_predict")]
        public int? NumPredict { get; init; }
    }
}
