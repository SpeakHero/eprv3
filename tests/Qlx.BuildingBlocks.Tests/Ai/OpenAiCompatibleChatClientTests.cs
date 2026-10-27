/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：OpenAI 兼容协议内核适配器单元测试（请求构造、BaseUrl 兜底与覆盖、配置缺失、错误截断、响应解析）
 */

using System.Net;
using System.Text;
using System.Text.Json;

using Qlx.Abstractions.Ai;
using Qlx.BuildingBlocks.Ai;
using Qlx.BuildingBlocks.Ai.Providers;

namespace Qlx.BuildingBlocks.Tests.Ai;

/// <summary>
/// <see cref="OpenAiCompatibleChatClient"/> 单元测试：请求报文契约、基地址解析优先级、
/// 厂商配置缺失时的失败信号以及响应/错误解析行为。
/// </summary>
public sealed class OpenAiCompatibleChatClientTests
{
    private const string SuccessResponseJson = """
        {"choices":[{"message":{"content":"hi"},"finish_reason":"stop"}],"usage":{"prompt_tokens":11,"completion_tokens":22}}
        """;

    private static AiProviderOptions Provider(
        string? baseUrl = "https://api.example.com/v1/",
        string? apiKey = "secret",
        string? model = "m1")
        => new()
        {
            Type = AiProviderTypes.OpenAiCompatible,
            BaseUrl = baseUrl,
            ApiKey = apiKey,
            Model = model,
        };

    private static OpenAiCompatibleChatClient CreateClient(
        StubHttpMessageHandler handler,
        AiProviderOptions provider,
        string? defaultBaseUrl = null)
        => new("p", provider, new StubHttpClientFactory(handler), defaultBaseUrl);

    [Fact]
    public async Task ChatAsyncBuildsOpenAiCompatibleRequest()
    {
        using StubHttpMessageHandler handler = StubHttpMessageHandler.RespondJson(SuccessResponseJson);
        AiChatRequest request = new()
        {
            Purpose = "unit-test",
            Messages =
            [
                new AiMessage(AiMessageRole.System, "你是有用的助手"),
                new AiMessage(AiMessageRole.User, "你好"),
            ],
            Temperature = 0.5,
            MaxTokens = 64,
            ResponseFormat = AiResponseFormat.Json,
        };

        AiChatResponse response = await CreateClient(handler, Provider())
            .ChatAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("hi", response.Content);
        Assert.Equal(1, handler.CallCount);

        RecordedHttpRequest recorded = handler.Requests[0];
        Assert.Equal("POST", recorded.Method);
        Assert.Equal("https://api.example.com/v1/chat/completions", recorded.Url);
        Assert.Equal("Bearer secret", recorded.Authorization);

        using JsonDocument document = JsonDocument.Parse(recorded.Body!);
        JsonElement root = document.RootElement;
        Assert.Equal("m1", root.GetProperty("model").GetString());
        Assert.Equal(64, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0.5, root.GetProperty("temperature").GetDouble());
        Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());

        JsonElement messages = root.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("你是有用的助手", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("你好", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ChatAsyncUsesDefaultBaseUrlWhenProviderBaseUrlIsEmpty()
    {
        using StubHttpMessageHandler handler = StubHttpMessageHandler.RespondJson(SuccessResponseJson);

        await CreateClient(handler, Provider(baseUrl: null), "https://default.example/v1/")
            .ChatAsync(AiTestData.Request(), TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.CallCount);
        Assert.Equal("https://default.example/v1/chat/completions", handler.Requests[0].Url);
    }

    [Fact]
    public async Task ChatAsyncPrefersProviderBaseUrlOverDefault()
    {
        using StubHttpMessageHandler handler = StubHttpMessageHandler.RespondJson(SuccessResponseJson);

        await CreateClient(handler, Provider(baseUrl: "https://api.example.com/v1/"), "https://default.example/v1/")
            .ChatAsync(AiTestData.Request(), TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.CallCount);
        Assert.Equal("https://api.example.com/v1/chat/completions", handler.Requests[0].Url);
    }

    [Fact]
    public async Task ChatAsyncRequestModelOverridesProviderModel()
    {
        using StubHttpMessageHandler handler = StubHttpMessageHandler.RespondJson(SuccessResponseJson);

        AiChatResponse response = await CreateClient(handler, Provider())
            .ChatAsync(AiTestData.Request(model: "req-model"), TestContext.Current.CancellationToken);

        Assert.Equal("req-model", response.Model);

        using JsonDocument document = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal("req-model", document.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task ChatAsyncThrowsWhenBothModelsAreEmpty()
    {
        using StubHttpMessageHandler handler = StubHttpMessageHandler.RespondJson(SuccessResponseJson);
        OpenAiCompatibleChatClient client = CreateClient(handler, Provider(model: null));

        AiProviderException exception = await Assert.ThrowsAsync<AiProviderException>(
            () => client.ChatAsync(AiTestData.Request(), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCodes.ProviderNotConfigured, exception.ErrorCode);
        Assert.Null(exception.HttpStatusCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ChatAsyncThrowsWhenApiKeyIsEmpty()
    {
        using StubHttpMessageHandler handler = StubHttpMessageHandler.RespondJson(SuccessResponseJson);
        OpenAiCompatibleChatClient client = CreateClient(handler, Provider(apiKey: null));

        AiProviderException exception = await Assert.ThrowsAsync<AiProviderException>(
            () => client.ChatAsync(AiTestData.Request(), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCodes.ProviderNotConfigured, exception.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ChatAsyncThrowsWhenBothBaseUrlsAreEmpty()
    {
        using StubHttpMessageHandler handler = StubHttpMessageHandler.RespondJson(SuccessResponseJson);
        OpenAiCompatibleChatClient client = CreateClient(handler, Provider(baseUrl: null));

        AiProviderException exception = await Assert.ThrowsAsync<AiProviderException>(
            () => client.ChatAsync(AiTestData.Request(), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCodes.ProviderNotConfigured, exception.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ChatAsyncThrowsHttpStatusErrorWithTruncatedBody()
    {
        using StubHttpMessageHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(new string('x', 1000), Encoding.UTF8, "text/plain"),
        });

        AiProviderException exception = await Assert.ThrowsAsync<AiProviderException>(
            () => CreateClient(handler, Provider())
                .ChatAsync(AiTestData.Request(), TestContext.Current.CancellationToken));

        Assert.Equal(429, exception.HttpStatusCode);
        Assert.Equal(AiErrorCodes.HttpStatus(429), exception.ErrorCode);
        Assert.StartsWith("HTTP 429: ", exception.Message, StringComparison.Ordinal);
        Assert.True(exception.Message.Length < 700);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatAsyncParsesContentUsageAndFinishReason()
    {
        using StubHttpMessageHandler handler = StubHttpMessageHandler.RespondJson(SuccessResponseJson);

        AiChatResponse response = await CreateClient(handler, Provider())
            .ChatAsync(AiTestData.Request(), TestContext.Current.CancellationToken);

        Assert.Equal("hi", response.Content);
        Assert.Equal("p", response.ProviderName);
        Assert.Equal("m1", response.Model);
        Assert.Equal(11, response.PromptTokens);
        Assert.Equal(22, response.CompletionTokens);
        Assert.Equal("stop", response.FinishReason);
        Assert.True(response.DurationMilliseconds >= 0);
    }

    [Fact]
    public async Task ChatAsyncThrowsInvalidResponseWhenContentIsMissing()
    {
        using StubHttpMessageHandler handler = StubHttpMessageHandler.RespondJson("{}");

        AiProviderException exception = await Assert.ThrowsAsync<AiProviderException>(
            () => CreateClient(handler, Provider())
                .ChatAsync(AiTestData.Request(), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCodes.InvalidResponse, exception.ErrorCode);
    }

    [Fact]
    public async Task ChatAsyncThrowsInvalidResponseWhenBodyIsNotJson()
    {
        using StubHttpMessageHandler handler = StubHttpMessageHandler.RespondJson("not-json");

        AiProviderException exception = await Assert.ThrowsAsync<AiProviderException>(
            () => CreateClient(handler, Provider())
                .ChatAsync(AiTestData.Request(), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCodes.InvalidResponse, exception.ErrorCode);
    }
}
