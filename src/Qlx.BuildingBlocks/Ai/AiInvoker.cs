/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 调用业务门面实现（固定 / 轮询策略、故障转移、结构化日志与标准化异常）
 */

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Qlx.Abstractions.Ai;
using Qlx.Abstractions.Exceptions;

using System.Diagnostics;
using System.Text.Json;

namespace Qlx.BuildingBlocks.Ai;

/// <summary>
/// AI 调用业务门面实现。按 <see cref="QlxAiOptions.Strategy"/> 解析候选厂商并逐个尝试，
/// 任一厂商返回有效结果立即终止后续轮询；全部失败抛出 <see cref="AiInvocationException"/> 并携带完整调用链。
/// 无状态、线程安全；适配器每次尝试即时创建，不持有可变状态。
/// </summary>
public sealed partial class AiInvoker : IAiInvoker
{
    /// <summary>异常消息在调用链摘要中的最大截断长度（字符）。</summary>
    private const int MaxSummaryMessageLength = 60;

    /// <summary>尝试明细中错误消息的最大截断长度（字符）。</summary>
    private const int MaxAttemptErrorMessageLength = 500;

    /// <summary>无效响应（空内容或非法 JSON）的统一提示。</summary>
    
    private const string InvalidResponseMessage = "响应内容为空或不是合法 JSON";

    private readonly QlxAiOptions _options;
    private readonly IAiChatClientFactory _clientFactory;
    private readonly ILogger<AiInvoker> _logger;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="options">AI 调用层全局配置。</param>
    /// <param name="clientFactory">AI 厂商适配器工厂。</param>
    /// <param name="logger">日志记录器。</param>
    public AiInvoker(IOptions<QlxAiOptions> options, IAiChatClientFactory clientFactory, ILogger<AiInvoker> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _clientFactory = clientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">消息列表为空时抛出（调用方编程错误）。</exception>
    /// <exception cref="AiInvocationException">AI 未启用、无可用厂商或全部候选厂商均失败时抛出。</exception>
    public async Task<AiChatResponse> InvokeAsync(AiChatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Messages is null || request.Messages.Count == 0)
        {
            throw new ArgumentException("AI 请求消息列表不能为空。", nameof(request));
        }

        if (!_options.Enabled)
        {
            throw new AiInvocationException(
                AiErrorCodes.Disabled,
                "AI 调用层未启用（Qlx:Ai:Enabled=false）。",
                []);
        }

        bool isFixed = string.Equals(_options.Strategy, QlxAiOptions.Strategies.Fixed, StringComparison.OrdinalIgnoreCase);
        List<ProviderCandidate> candidates = ResolveCandidates(isFixed);

        if (candidates.Count == 0)
        {
            throw new AiInvocationException(
                AiErrorCodes.ProviderNotConfigured,
                "未配置任何启用的 AI 厂商。",
                []);
        }

        string correlationId = Guid.NewGuid().ToString("N")[..12];
        long startTimestamp = Stopwatch.GetTimestamp();

        LogInvocationStart(
            correlationId,
            request.Purpose,
            _options.Strategy,
            candidates.Count,
            request.Model,
            request.Temperature,
            request.MaxTokens,
            request.ResponseFormat,
            request.Messages.Count,
            BuildContentPreview(request));

        List<AiInvocationAttempt> attempts = [];
        Exception? lastException = null;

        for (int index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ProviderCandidate candidate = candidates[index];
            string? model = request.Model ?? candidate.Options.Model;
            int attemptIndex = index + 1;
            long attemptStartTimestamp = Stopwatch.GetTimestamp();

            try
            {
                IAiChatClient client = _clientFactory.Create(candidate.Name);
                AiChatResponse response = await client.ChatAsync(request, cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(response.Content)
                    || (request.ResponseFormat == AiResponseFormat.Json && !IsValidJson(response.Content)))
                {
                    RecordFailure(
                        attempts,
                        correlationId,
                        attemptIndex,
                        candidate,
                        model,
                        attemptStartTimestamp,
                        httpStatusCode: null,
                        AiErrorCodes.InvalidResponse,
                        InvalidResponseMessage);
                    continue;
                }

                long durationMilliseconds = ElapsedMilliseconds(attemptStartTimestamp);
                AiChatResponse result = response with { DurationMilliseconds = durationMilliseconds };

                attempts.Add(new AiInvocationAttempt(
                    attemptIndex,
                    candidate.Name,
                    candidate.Options.Type,
                    model,
                    durationMilliseconds,
                    AiAttemptOutcome.Success,
                    null,
                    null,
                    null));

                LogProviderSucceeded(
                    correlationId,
                    candidate.Name,
                    candidate.Options.Type,
                    model,
                    attemptIndex,
                    durationMilliseconds,
                    null,
                    result.Content.Length);

                LogInvocationCompleted(
                    correlationId,
                    _options.Strategy,
                    attempts.Count,
                    ElapsedMilliseconds(startTimestamp),
                    candidate.Name);

                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 外部令牌取消：原样上抛，不包装为业务异常
                throw;
            }
            catch (AiProviderException providerException)
            {
                lastException = providerException;
                RecordFailure(
                    attempts,
                    correlationId,
                    attemptIndex,
                    candidate,
                    model,
                    attemptStartTimestamp,
                    providerException.HttpStatusCode,
                    providerException.ErrorCode,
                    providerException.Message);
            }
            catch (TaskCanceledException timeoutException)
            {
                lastException = timeoutException;
                RecordFailure(
                    attempts,
                    correlationId,
                    attemptIndex,
                    candidate,
                    model,
                    attemptStartTimestamp,
                    httpStatusCode: null,
                    AiErrorCodes.Timeout,
                    "调用超时");
            }
            catch (HttpRequestException networkException)
            {
                lastException = networkException;
                RecordFailure(
                    attempts,
                    correlationId,
                    attemptIndex,
                    candidate,
                    model,
                    attemptStartTimestamp,
                    httpStatusCode: null,
                    AiErrorCodes.NetworkError,
                    networkException.Message);
            }
            catch (Exception unexpectedException)
            {
                lastException = unexpectedException;
                RecordFailure(
                    attempts,
                    correlationId,
                    attemptIndex,
                    candidate,
                    model,
                    attemptStartTimestamp,
                    httpStatusCode: null,
                    AiErrorCodes.UnexpectedError,
                    unexpectedException.Message);
            }
        }

        string chain = string.Join("；", attempts.Select(static attempt => $"{attempt.ProviderName}({Summarize(attempt)})"));
        string message = $"AI 调用全部失败（策略 {_options.Strategy}，尝试 {attempts.Count} 个厂商）：{chain}";
        string errorCodes = string.Join(
            ", ",
            attempts.Select(static attempt => attempt.ErrorCode ?? AiErrorCodes.UnexpectedError));

        LogInvocationFailed(correlationId, _options.Strategy, attempts.Count, ElapsedMilliseconds(startTimestamp), errorCodes);

        throw new AiInvocationException(AiErrorCodes.InvocationFailed, message, attempts, lastException);
    }

    private void RecordFailure(
        List<AiInvocationAttempt> attempts,
        string correlationId,
        int attemptIndex,
        ProviderCandidate candidate,
        string? model,
        long attemptStartTimestamp,
        int? httpStatusCode,
        string errorCode,
        string errorMessage)
    {
        long durationMilliseconds = ElapsedMilliseconds(attemptStartTimestamp);
        string truncatedMessage = Truncate(errorMessage, MaxAttemptErrorMessageLength);

        attempts.Add(new AiInvocationAttempt(
            attemptIndex,
            candidate.Name,
            candidate.Options.Type,
            model,
            durationMilliseconds,
            AiAttemptOutcome.Failed,
            httpStatusCode,
            errorCode,
            truncatedMessage));

        LogProviderFailed(
            correlationId,
            candidate.Name,
            candidate.Options.Type,
            model,
            attemptIndex,
            durationMilliseconds,
            AiAttemptOutcome.Failed,
            httpStatusCode,
            errorCode,
            truncatedMessage);
    }

    private List<ProviderCandidate> ResolveCandidates(bool isFixed)
    {
        List<ProviderCandidate> candidates = [];

        if (isFixed && !string.IsNullOrWhiteSpace(_options.FixedProvider))
        {
            ProviderCandidate? target = null;
            foreach ((string providerName, AiProviderOptions providerOptions) in _options.Providers)
            {
                if (string.Equals(providerName, _options.FixedProvider, StringComparison.OrdinalIgnoreCase))
                {
                    target = new ProviderCandidate(providerName, providerOptions);
                    break;
                }
            }

            if (target is null || !target.Options.Enabled)
            {
                throw new AiInvocationException(
                    AiErrorCodes.ProviderNotConfigured,
                    $"固定模式目标厂商未配置或已停用：{_options.FixedProvider}",
                    []);
            }

            candidates.Add(target);
            return candidates;
        }

        foreach ((string providerName, AiProviderOptions providerOptions) in _options.Providers)
        {
            if (providerOptions.Enabled)
            {
                candidates.Add(new ProviderCandidate(providerName, providerOptions));
            }
        }

        candidates.Sort(static (left, right) =>
        {
            int priorityComparison = left.Options.Priority.CompareTo(right.Options.Priority);
            return priorityComparison != 0
                ? priorityComparison
                : StringComparer.Ordinal.Compare(left.Name, right.Name);
        });

        if (isFixed && candidates.Count > 1)
        {
            ProviderCandidate first = candidates[0];
            candidates.Clear();
            candidates.Add(first);
        }

        return candidates;
    }

    private string? BuildContentPreview(AiChatRequest request)
    {
        if (!_options.LogRequestContent)
        {
            return null;
        }

        string joined = string.Join(Environment.NewLine, request.Messages.Select(static message => $"{message.Role}: {message.Content}"));
        int maxLength = _options.MaxLogContentLength;
        return joined.Length <= maxLength ? joined : joined[..maxLength];
    }

    private static bool IsValidJson(string content)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Summarize(AiInvocationAttempt attempt)
    {
        string message = Truncate(attempt.ErrorMessage, MaxSummaryMessageLength);
        return string.IsNullOrEmpty(message) ? attempt.ErrorCode ?? string.Empty : $"{attempt.ErrorCode} {message}";
    }

    private static string Truncate(string? value, int maxLength)
    {
        return string.IsNullOrEmpty(value) ? string.Empty : value.Length <= maxLength ? value : value[..maxLength];
    }

    private static long ElapsedMilliseconds(long startTimestamp)
    {
        return (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
    }

    [LoggerMessage(
        EventId = 4101,
        Level = LogLevel.Information,
        Message = "AI 调用开始 [{CorrelationId}] purpose={Purpose} strategy={Strategy} candidates={CandidateCount} model={Model} temperature={Temperature} maxTokens={MaxTokens} format={ResponseFormat} messageCount={MessageCount} contentPreview={ContentPreview}")]
    private partial void LogInvocationStart(
        string correlationId,
        string? purpose,
        string strategy,
        int candidateCount,
        string? model,
        double? temperature,
        int? maxTokens,
        AiResponseFormat responseFormat,
        int messageCount,
        string? contentPreview);

    [LoggerMessage(
        EventId = 4102,
        Level = LogLevel.Information,
        Message = "AI 厂商调用成功 [{CorrelationId}] provider={Provider} type={ProviderType} model={Model} attempt={AttemptIndex} durationMs={DurationMilliseconds} httpStatus={HttpStatusCode} contentLength={ContentLength}")]
    private partial void LogProviderSucceeded(
        string correlationId,
        string provider,
        string providerType,
        string? model,
        int attemptIndex,
        long durationMilliseconds,
        int? httpStatusCode,
        int contentLength);

    [LoggerMessage(
        EventId = 4103,
        Level = LogLevel.Warning,
        Message = "AI 厂商调用失败 [{CorrelationId}] provider={Provider} type={ProviderType} model={Model} attempt={AttemptIndex} durationMs={DurationMilliseconds} outcome={Outcome} httpStatus={HttpStatusCode} errorCode={ErrorCode} errorMessage={ErrorMessage}")]
    private partial void LogProviderFailed(
        string correlationId,
        string provider,
        string providerType,
        string? model,
        int attemptIndex,
        long durationMilliseconds,
        AiAttemptOutcome outcome,
        int? httpStatusCode,
        string errorCode,
        string errorMessage);

    [LoggerMessage(
        EventId = 4104,
        Level = LogLevel.Information,
        Message = "AI 调用完成 [{CorrelationId}] strategy={Strategy} attempts={AttemptCount} totalMs={TotalDurationMilliseconds} provider={Provider}")]
    private partial void LogInvocationCompleted(
        string correlationId,
        string strategy,
        int attemptCount,
        long totalDurationMilliseconds,
        string provider);

    [LoggerMessage(
        EventId = 4105,
        Level = LogLevel.Error,
        Message = "AI 调用失败 [{CorrelationId}] strategy={Strategy} attempts={AttemptCount} totalMs={TotalDurationMilliseconds} errorCodes={ErrorCodes}")]
    private partial void LogInvocationFailed(
        string correlationId,
        string strategy,
        int attemptCount,
        long totalDurationMilliseconds,
        string errorCodes);

    /// <summary>
    /// 候选厂商（配置中的厂商名与配置项）。
    /// </summary>
    /// <param name="Name">厂商名（<c>Qlx:Ai:Providers</c> 的键）。</param>
    /// <param name="Options">该厂商的配置项。</param>
    private sealed record ProviderCandidate(string Name, AiProviderOptions Options);
}
