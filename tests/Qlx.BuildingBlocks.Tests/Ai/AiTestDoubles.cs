/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 调用层共享测试替身——记录型适配器 / 假厂商工厂 / HTTP 桩 / 日志捕获器
 */

using System.Net.Http;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Qlx.Abstractions.Ai;
using Qlx.BuildingBlocks.Ai;

namespace Qlx.BuildingBlocks.Tests.Ai;

/// <summary>
/// 记录型厂商适配器：记录每次调用请求，按注入委托返回响应或抛出异常。
/// </summary>
internal sealed class RecordingAiChatClient : IAiChatClient
{
    private readonly Func<AiChatRequest, CancellationToken, Task<AiChatResponse>> _behavior;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="providerName">厂商名。</param>
    /// <param name="behavior">调用行为委托。</param>
    public RecordingAiChatClient(string providerName, Func<AiChatRequest, CancellationToken, Task<AiChatResponse>> behavior)
    {
        ProviderName = providerName;
        _behavior = behavior;
    }

    /// <inheritdoc />
    public string ProviderName { get; }

    /// <summary>全部调用请求（按发生顺序）。</summary>
    public List<AiChatRequest> Requests { get; } = [];

    /// <summary>调用次数。</summary>
    public int CallCount => Requests.Count;

    /// <inheritdoc />
    public Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        cancellationToken.ThrowIfCancellationRequested();
        return _behavior(request, cancellationToken);
    }

    /// <summary>
    /// 构造恒成功适配器。
    /// </summary>
    /// <param name="providerName">厂商名。</param>
    /// <param name="content">响应正文。</param>
    /// <returns>适配器实例。</returns>
    public static RecordingAiChatClient Succeed(string providerName, string content = "ok")
        => new(providerName, (_, _) => Task.FromResult(
            new AiChatResponse(content, providerName, "test-model", 0, 1, 2, "stop")));

    /// <summary>
    /// 构造恒失败适配器。
    /// </summary>
    /// <param name="providerName">厂商名。</param>
    /// <param name="exception">抛出异常（每次调用重新抛出同一实例）。</param>
    /// <returns>适配器实例。</returns>
    public static RecordingAiChatClient Fail(string providerName, Exception exception)
        => new(providerName, (_, _) => Task.FromException<AiChatResponse>(exception));

    /// <summary>
    /// 构造按委托返回的适配器。
    /// </summary>
    /// <param name="providerName">厂商名。</param>
    /// <param name="behavior">调用行为委托。</param>
    /// <returns>适配器实例。</returns>
    public static RecordingAiChatClient From(string providerName, Func<AiChatRequest, CancellationToken, Task<AiChatResponse>> behavior)
        => new(providerName, behavior);
}

/// <summary>
/// 假厂商适配器工厂：按 <see cref="Type"/> 匹配，按厂商名返回预注册的适配器实例。
/// </summary>
internal sealed class FakeAiChatClientProvider : IAiChatClientProvider
{
    private readonly Dictionary<string, IAiChatClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="type">厂商类型标识。</param>
    /// <param name="defaultBaseUrl">默认基地址（可为空）。</param>
    public FakeAiChatClientProvider(string type, string? defaultBaseUrl = null)
    {
        Type = type;
        DefaultBaseUrl = defaultBaseUrl;
    }

    /// <inheritdoc />
    public string Type { get; }

    /// <inheritdoc />
    public string? DefaultBaseUrl { get; }

    /// <summary>被请求创建的厂商名（按发生顺序）。</summary>
    public List<string> CreatedProviderNames { get; } = [];

    /// <summary>
    /// 预注册厂商名 → 适配器实例。
    /// </summary>
    /// <param name="providerName">厂商名。</param>
    /// <param name="client">适配器实例。</param>
    /// <returns>自身（支持链式注册）。</returns>
    public FakeAiChatClientProvider Register(string providerName, IAiChatClient client)
    {
        _clients[providerName] = client;
        return this;
    }

    /// <inheritdoc />
    public IAiChatClient Create(string providerName, AiProviderOptions provider)
    {
        CreatedProviderNames.Add(providerName);

        if (_clients.TryGetValue(providerName, out IAiChatClient? client))
        {
            return client;
        }

        throw new InvalidOperationException($"测试未预注册该厂商的适配器：{providerName}");
    }
}

/// <summary>
/// 记录型 HTTP 请求（适配器级单测断言用）。
/// </summary>
/// <param name="Method">HTTP 方法。</param>
/// <param name="Url">请求地址。</param>
/// <param name="Body">请求正文。</param>
/// <param name="Authorization">Authorization 头。</param>
internal sealed record RecordedHttpRequest(string Method, string? Url, string? Body, string? Authorization);

/// <summary>
/// HTTP 桩处理器：记录请求并按委托返回响应。
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="responder">响应委托。</param>
    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    /// <summary>全部请求记录（按发生顺序）。</summary>
    public List<RecordedHttpRequest> Requests { get; } = [];

    /// <summary>请求次数。</summary>
    public int CallCount => Requests.Count;

    /// <summary>
    /// 构造恒返回指定 JSON 正文（HTTP 200）的桩。
    /// </summary>
    /// <param name="json">响应正文。</param>
    /// <returns>桩处理器。</returns>
    public static StubHttpMessageHandler RespondJson(string json)
        => new(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        Requests.Add(new RecordedHttpRequest(
            request.Method.Method,
            request.RequestUri?.ToString(),
            body,
            request.Headers.Authorization?.ToString()));

        return _responder(request);
    }
}

/// <summary>
/// 单桩 <see cref="IHttpClientFactory"/>：所有命名客户端共享同一个桩处理器。
/// </summary>
internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    /// <summary>被请求创建的命名客户端名称（按发生顺序）。</summary>
    public List<string> RequestedNames { get; } = [];

    /// <inheritdoc />
    public HttpClient CreateClient(string name)
    {
        RequestedNames.Add(name);
        return new HttpClient(handler, disposeHandler: false);
    }
}

/// <summary>
/// 日志捕获器：留存每条日志的级别、事件 Id、格式化消息与结构化字段。
/// </summary>
/// <typeparam name="T">日志类别类型。</typeparam>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    /// <summary>全部日志条目（按发生顺序）。</summary>
    public List<CapturedLogEntry> Entries { get; } = [];

    /// <inheritdoc />
    public IDisposable BeginScope<TState>(TState state) where TState : notnull
        => NullScope.Instance;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Dictionary<string, object?> fields = new(StringComparer.Ordinal);
        if (state is IEnumerable<KeyValuePair<string, object?>> structured)
        {
            foreach (KeyValuePair<string, object?> pair in structured)
            {
                fields[pair.Key] = pair.Value;
            }
        }

        Entries.Add(new CapturedLogEntry(logLevel, eventId.Id, formatter(state, exception), fields, exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// 捕获到的单条日志。
/// </summary>
/// <param name="Level">日志级别。</param>
/// <param name="EventId">事件 Id。</param>
/// <param name="Message">格式化消息。</param>
/// <param name="Fields">结构化字段。</param>
/// <param name="Exception">异常（可为空）。</param>
internal sealed record CapturedLogEntry(
    LogLevel Level,
    int EventId,
    string Message,
    IReadOnlyDictionary<string, object?> Fields,
    Exception? Exception)
{
    /// <summary>
    /// 取结构化字段文本（缺失时返回空串）。
    /// </summary>
    /// <param name="name">字段名。</param>
    /// <returns>字段值文本。</returns>
    public string Field(string name) => Fields.TryGetValue(name, out object? value) ? value?.ToString() ?? string.Empty : string.Empty;
}

/// <summary>
/// AI 测试数据构造助手。
/// </summary>
internal static class AiTestData
{
    /// <summary>
    /// 构造对话请求。
    /// </summary>
    /// <param name="content">用户消息正文。</param>
    /// <param name="format">响应格式。</param>
    /// <param name="model">模型名（可为空）。</param>
    /// <returns>对话请求。</returns>
    public static AiChatRequest Request(
        string content = "你好",
        AiResponseFormat format = AiResponseFormat.Text,
        string? model = null)
        => new()
        {
            Purpose = "unit-test",
            Messages = [new AiMessage(AiMessageRole.User, content)],
            Model = model,
            Temperature = 0.2,
            MaxTokens = 128,
            ResponseFormat = format,
        };

    /// <summary>
    /// 构造全局配置。
    /// </summary>
    /// <param name="enabled">是否启用。</param>
    /// <param name="strategy">调用策略。</param>
    /// <param name="fixedProvider">固定模式目标厂商。</param>
    /// <param name="providers">厂商配置（厂商名 → 配置项）。</param>
    /// <returns>全局配置。</returns>
    public static QlxAiOptions Options(
        bool enabled = true,
        string strategy = QlxAiOptions.Strategies.Fixed,
        string? fixedProvider = null,
        Dictionary<string, AiProviderOptions>? providers = null)
        => new()
        {
            Enabled = enabled,
            Strategy = strategy,
            FixedProvider = fixedProvider,
            Providers = providers ?? [],
        };

    /// <summary>
    /// 包装为 <see cref="IOptions{TOptions}"/>。
    /// </summary>
    /// <param name="options">全局配置。</param>
    /// <returns>选项包装。</returns>
    public static IOptions<QlxAiOptions> Wrap(QlxAiOptions options) => Microsoft.Extensions.Options.Options.Create(options);
}
