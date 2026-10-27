/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 调用门面策略/故障转移/日志单测
 */

using System.Globalization;

using Microsoft.Extensions.Logging;

using Qlx.Abstractions.Ai;
using Qlx.Abstractions.Exceptions;
using Qlx.BuildingBlocks.Ai;

namespace Qlx.BuildingBlocks.Tests.Ai;

/// <summary>
/// <see cref="AiInvoker"/> 单元测试：固定 / 轮询策略、故障转移、失败分类、结构化日志与密钥保护。
/// </summary>
public sealed class AiInvokerTests
{
    private const string SecretApiKey = "secret-key-123";

    private static (AiInvoker Invoker, CapturingLogger<AiInvoker> Logger) Create(QlxAiOptions options, params IAiChatClientProvider[] providers)
    {
        var logger = new CapturingLogger<AiInvoker>();
        return (new AiInvoker(AiTestData.Wrap(options), new AiChatClientFactory(AiTestData.Wrap(options), providers), logger), logger);
    }

    private static Dictionary<string, AiProviderOptions> ProviderMap(params (string Name, int Priority)[] entries)
    {
        Dictionary<string, AiProviderOptions> providers = [];
        foreach ((string name, int priority) in entries)
        {
            providers[name] = new AiProviderOptions { Priority = priority };
        }

        return providers;
    }

    private static CapturedLogEntry Entry(CapturingLogger<AiInvoker> logger, int eventId)
        => logger.Entries.Single(entry => entry.EventId == eventId);

    private static CapturedLogEntry EntryFor(CapturingLogger<AiInvoker> logger, int eventId, string providerName)
        => logger.Entries.Single(entry => entry.EventId == eventId
            && string.Equals(entry.Field("Provider"), providerName, StringComparison.Ordinal));

    private static long FieldLong(CapturedLogEntry entry, string fieldName)
    {
        string raw = entry.Field(fieldName);
        Assert.True(
            long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value),
            $"日志字段 {fieldName} 不是整数：{raw}");
        return value;
    }

    [Fact]
    public async Task FixedStrategySuccessCallsOnlyConfiguredProvider()
    {
        RecordingAiChatClient p1 = RecordingAiChatClient.Succeed("p1");
        RecordingAiChatClient p2 = RecordingAiChatClient.Succeed("p2");
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible)
            .Register("p1", p1)
            .Register("p2", p2);

        QlxAiOptions options = AiTestData.Options(
            fixedProvider: "p1",
            providers: ProviderMap(("p1", 100), ("p2", 200)));

        (AiInvoker invoker, _) = Create(options, fakeProvider);

        AiChatResponse response = await invoker.InvokeAsync(AiTestData.Request(), TestContext.Current.CancellationToken);

        Assert.Equal("p1", response.ProviderName);
        Assert.Equal(1, p1.CallCount);
        Assert.Equal(0, p2.CallCount);
        Assert.True(response.DurationMilliseconds >= 0, "耗时字段应为非负");
    }

    [Fact]
    public async Task FixedStrategyFailureDoesNotFailOver()
    {
        RecordingAiChatClient p1 = RecordingAiChatClient.Fail("p1", new AiProviderException("p1", "HTTP_500", "boom", 500));
        RecordingAiChatClient p2 = RecordingAiChatClient.Succeed("p2");
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible)
            .Register("p1", p1)
            .Register("p2", p2);

        QlxAiOptions options = AiTestData.Options(
            fixedProvider: "p1",
            providers: ProviderMap(("p1", 100), ("p2", 200)));

        (AiInvoker invoker, _) = Create(options, fakeProvider);

        AiInvocationException exception = await Assert.ThrowsAsync<AiInvocationException>(
            () => invoker.InvokeAsync(AiTestData.Request(), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCodes.InvocationFailed, exception.ErrorCode);
        Assert.Single(exception.Attempts);
        Assert.Equal("p1", exception.Attempts[0].ProviderName);
        Assert.Equal(1, p1.CallCount);
        Assert.Equal(0, p2.CallCount);
    }

    [Fact]
    public async Task RoundRobinFailoverStopsAtFirstSuccessfulProvider()
    {
        RecordingAiChatClient p1 = RecordingAiChatClient.Fail("p1", new AiProviderException("p1", "HTTP_500", "boom", 500));
        RecordingAiChatClient p2 = RecordingAiChatClient.Succeed("p2");
        RecordingAiChatClient p3 = RecordingAiChatClient.Succeed("p3");
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible)
            .Register("p1", p1)
            .Register("p2", p2)
            .Register("p3", p3);

        QlxAiOptions options = AiTestData.Options(
            strategy: QlxAiOptions.Strategies.RoundRobin,
            providers: ProviderMap(("p1", 10), ("p2", 20), ("p3", 30)));

        (AiInvoker invoker, _) = Create(options, fakeProvider);

        AiChatResponse response = await invoker.InvokeAsync(AiTestData.Request(), TestContext.Current.CancellationToken);

        Assert.Equal("p2", response.ProviderName);
        Assert.Equal(1, p1.CallCount);
        Assert.Equal(1, p2.CallCount);
        Assert.Equal(0, p3.CallCount);
    }

    [Fact]
    public async Task RoundRobinAllProvidersFailAggregatesAttempts()
    {
        RecordingAiChatClient p1 = RecordingAiChatClient.Fail("p1", new AiProviderException("p1", "HTTP_500", "boom", 500));
        RecordingAiChatClient p2 = RecordingAiChatClient.Fail("p2", new AiProviderException("p2", "HTTP_429", "slow down", 429));
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible)
            .Register("p1", p1)
            .Register("p2", p2);

        QlxAiOptions options = AiTestData.Options(
            strategy: QlxAiOptions.Strategies.RoundRobin,
            providers: ProviderMap(("p1", 10), ("p2", 20)));

        (AiInvoker invoker, _) = Create(options, fakeProvider);

        AiInvocationException exception = await Assert.ThrowsAsync<AiInvocationException>(
            () => invoker.InvokeAsync(AiTestData.Request(), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCodes.InvocationFailed, exception.ErrorCode);
        Assert.Contains("p1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("p2", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, exception.Attempts.Count);

        AiInvocationAttempt first = exception.Attempts[0];
        Assert.Equal(1, first.Index);
        Assert.Equal("HTTP_500", first.ErrorCode);
        Assert.Equal(500, first.HttpStatusCode);
        Assert.Equal(AiAttemptOutcome.Failed, first.Outcome);
        Assert.True(first.DurationMilliseconds >= 0, "耗时字段应为非负");

        AiInvocationAttempt second = exception.Attempts[1];
        Assert.Equal(2, second.Index);
        Assert.Equal("HTTP_429", second.ErrorCode);
        Assert.Equal(429, second.HttpStatusCode);
        Assert.Equal(AiAttemptOutcome.Failed, second.Outcome);
        Assert.True(second.DurationMilliseconds >= 0, "耗时字段应为非负");

        Assert.All(exception.Attempts, static attempt => Assert.Equal(AiAttemptOutcome.Failed, attempt.Outcome));
    }

    [Fact]
    public async Task RoundRobinContinuesOnUnauthorizedForbiddenAndRateLimited()
    {
        RecordingAiChatClient p1 = RecordingAiChatClient.Fail("p1", new AiProviderException("p1", "HTTP_401", "unauthorized", 401));
        RecordingAiChatClient p2 = RecordingAiChatClient.Fail("p2", new AiProviderException("p2", "HTTP_403", "forbidden", 403));
        RecordingAiChatClient p3 = RecordingAiChatClient.Fail("p3", new AiProviderException("p3", "HTTP_429", "too many", 429));
        RecordingAiChatClient p4 = RecordingAiChatClient.Succeed("p4");
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible)
            .Register("p1", p1)
            .Register("p2", p2)
            .Register("p3", p3)
            .Register("p4", p4);

        QlxAiOptions options = AiTestData.Options(
            strategy: QlxAiOptions.Strategies.RoundRobin,
            providers: ProviderMap(("p1", 10), ("p2", 20), ("p3", 30), ("p4", 40)));

        (AiInvoker invoker, _) = Create(options, fakeProvider);

        AiChatResponse response = await invoker.InvokeAsync(AiTestData.Request(), TestContext.Current.CancellationToken);

        Assert.Equal("p4", response.ProviderName);
        Assert.Equal(1, p1.CallCount);
        Assert.Equal(1, p2.CallCount);
        Assert.Equal(1, p3.CallCount);
        Assert.Equal(1, p4.CallCount);
    }

    [Fact]
    public async Task EmptyContentIsInvalidResponseAndFailsOver()
    {
        RecordingAiChatClient p1 = RecordingAiChatClient.Succeed("p1", "   ");
        RecordingAiChatClient p2 = RecordingAiChatClient.Succeed("p2");
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible)
            .Register("p1", p1)
            .Register("p2", p2);

        QlxAiOptions options = AiTestData.Options(
            strategy: QlxAiOptions.Strategies.RoundRobin,
            providers: ProviderMap(("p1", 10), ("p2", 20)));

        (AiInvoker invoker, CapturingLogger<AiInvoker> logger) = Create(options, fakeProvider);

        AiChatResponse response = await invoker.InvokeAsync(AiTestData.Request(), TestContext.Current.CancellationToken);

        Assert.Equal("p2", response.ProviderName);
        Assert.Equal(1, p1.CallCount);
        Assert.Equal(1, p2.CallCount);
        Assert.Equal(AiErrorCodes.InvalidResponse, EntryFor(logger, 4103, "p1").Field("ErrorCode"));
    }

    [Fact]
    public async Task NonJsonContentIsInvalidResponseAndFailsOver()
    {
        RecordingAiChatClient p1 = RecordingAiChatClient.Succeed("p1", "not-json");
        RecordingAiChatClient p2 = RecordingAiChatClient.Succeed("p2", """{"a":1}""");
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible)
            .Register("p1", p1)
            .Register("p2", p2);

        QlxAiOptions options = AiTestData.Options(
            strategy: QlxAiOptions.Strategies.RoundRobin,
            providers: ProviderMap(("p1", 10), ("p2", 20)));

        (AiInvoker invoker, CapturingLogger<AiInvoker> logger) = Create(options, fakeProvider);

        AiChatResponse response = await invoker.InvokeAsync(
            AiTestData.Request(format: AiResponseFormat.Json),
            TestContext.Current.CancellationToken);

        Assert.Equal("p2", response.ProviderName);
        Assert.Equal("""{"a":1}""", response.Content);
        Assert.Equal(AiErrorCodes.InvalidResponse, EntryFor(logger, 4103, "p1").Field("ErrorCode"));
    }

    [Fact]
    public async Task ValidJsonContentSkipsFailover()
    {
        RecordingAiChatClient p1 = RecordingAiChatClient.Succeed("p1", """{"a":1}""");
        RecordingAiChatClient p2 = RecordingAiChatClient.Succeed("p2");
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible)
            .Register("p1", p1)
            .Register("p2", p2);

        QlxAiOptions options = AiTestData.Options(
            strategy: QlxAiOptions.Strategies.RoundRobin,
            providers: ProviderMap(("p1", 10), ("p2", 20)));

        (AiInvoker invoker, _) = Create(options, fakeProvider);

        AiChatResponse response = await invoker.InvokeAsync(
            AiTestData.Request(format: AiResponseFormat.Json),
            TestContext.Current.CancellationToken);

        Assert.Equal("p1", response.ProviderName);
        Assert.Equal(0, p2.CallCount);
    }

    [Fact]
    public async Task TimeoutIsClassifiedAndFailsOver()
    {
        RecordingAiChatClient p1 = RecordingAiChatClient.Fail("p1", new TaskCanceledException("timeout"));
        RecordingAiChatClient p2 = RecordingAiChatClient.Succeed("p2");
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible)
            .Register("p1", p1)
            .Register("p2", p2);

        QlxAiOptions options = AiTestData.Options(
            strategy: QlxAiOptions.Strategies.RoundRobin,
            providers: ProviderMap(("p1", 10), ("p2", 20)));

        (AiInvoker invoker, CapturingLogger<AiInvoker> logger) = Create(options, fakeProvider);

        AiChatResponse response = await invoker.InvokeAsync(AiTestData.Request(), TestContext.Current.CancellationToken);

        Assert.Equal("p2", response.ProviderName);
        Assert.Equal(1, p2.CallCount);
        Assert.Equal(AiErrorCodes.Timeout, EntryFor(logger, 4103, "p1").Field("ErrorCode"));
    }

    [Fact]
    public async Task NetworkAndUnexpectedExceptionsAreClassified()
    {
        RecordingAiChatClient p1 = RecordingAiChatClient.Fail("p1", new HttpRequestException("connection refused"));
        RecordingAiChatClient p2 = RecordingAiChatClient.Fail("p2", new InvalidOperationException("boom"));
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible)
            .Register("p1", p1)
            .Register("p2", p2);

        QlxAiOptions options = AiTestData.Options(
            strategy: QlxAiOptions.Strategies.RoundRobin,
            providers: ProviderMap(("p1", 10), ("p2", 20)));

        (AiInvoker invoker, _) = Create(options, fakeProvider);

        AiInvocationException exception = await Assert.ThrowsAsync<AiInvocationException>(
            () => invoker.InvokeAsync(AiTestData.Request(), TestContext.Current.CancellationToken));

        Assert.Equal(2, exception.Attempts.Count);
        Assert.Equal(AiErrorCodes.NetworkError, exception.Attempts[0].ErrorCode);
        Assert.Equal(AiErrorCodes.UnexpectedError, exception.Attempts[1].ErrorCode);
    }

    [Fact]
    public async Task AlreadyCanceledTokenIsNotWrapped()
    {
        RecordingAiChatClient p1 = RecordingAiChatClient.Succeed("p1");
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible).Register("p1", p1);

        QlxAiOptions options = AiTestData.Options(
            fixedProvider: "p1",
            providers: ProviderMap(("p1", 100)));

        (AiInvoker invoker, _) = Create(options, fakeProvider);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => invoker.InvokeAsync(AiTestData.Request(), cts.Token));

        Assert.IsNotType<AiInvocationException>(exception);
        Assert.Equal(0, p1.CallCount);
    }

    [Fact]
    public async Task AdapterCancellationIsNotWrapped()
    {
        using CancellationTokenSource cts = new();
        RecordingAiChatClient p1 = RecordingAiChatClient.From("p1", (_, _) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        RecordingAiChatClient p2 = RecordingAiChatClient.Succeed("p2");
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible)
            .Register("p1", p1)
            .Register("p2", p2);

        QlxAiOptions options = AiTestData.Options(
            strategy: QlxAiOptions.Strategies.RoundRobin,
            providers: ProviderMap(("p1", 10), ("p2", 20)));

        (AiInvoker invoker, _) = Create(options, fakeProvider);

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => invoker.InvokeAsync(AiTestData.Request(), cts.Token));

        Assert.IsNotType<AiInvocationException>(exception);
        Assert.Equal(1, p1.CallCount);
        Assert.Equal(0, p2.CallCount);
    }

    [Fact]
    public async Task DisabledOptionThrowsDisabled()
    {
        RecordingAiChatClient p1 = RecordingAiChatClient.Succeed("p1");
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible).Register("p1", p1);

        QlxAiOptions options = AiTestData.Options(
            enabled: false,
            fixedProvider: "p1",
            providers: ProviderMap(("p1", 100)));

        (AiInvoker invoker, _) = Create(options, fakeProvider);

        AiInvocationException exception = await Assert.ThrowsAsync<AiInvocationException>(
            () => invoker.InvokeAsync(AiTestData.Request(), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCodes.Disabled, exception.ErrorCode);
        Assert.Equal(0, p1.CallCount);
    }

    [Fact]
    public async Task EmptyMessagesThrowsArgumentException()
    {
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible);

        QlxAiOptions options = AiTestData.Options(
            fixedProvider: "p1",
            providers: ProviderMap(("p1", 100)));

        (AiInvoker invoker, _) = Create(options, fakeProvider);

        AiChatRequest request = new() { Messages = [] };

        await Assert.ThrowsAsync<ArgumentException>(
            () => invoker.InvokeAsync(request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NoEnabledProviderThrowsProviderNotConfigured()
    {
        RecordingAiChatClient p1 = RecordingAiChatClient.Succeed("p1");
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible).Register("p1", p1);

        QlxAiOptions options = AiTestData.Options(fixedProvider: "p1");

        (AiInvoker invoker, _) = Create(options, fakeProvider);

        AiInvocationException exception = await Assert.ThrowsAsync<AiInvocationException>(
            () => invoker.InvokeAsync(AiTestData.Request(), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCodes.ProviderNotConfigured, exception.ErrorCode);
        Assert.Empty(exception.Attempts);
        Assert.Equal(0, p1.CallCount);
    }

    [Fact]
    public async Task FixedProviderMissingThrowsProviderNotConfigured()
    {
        RecordingAiChatClient p1 = RecordingAiChatClient.Succeed("p1");
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible).Register("p1", p1);

        QlxAiOptions options = AiTestData.Options(
            fixedProvider: "missing",
            providers: ProviderMap(("p1", 100)));

        (AiInvoker invoker, _) = Create(options, fakeProvider);

        AiInvocationException exception = await Assert.ThrowsAsync<AiInvocationException>(
            () => invoker.InvokeAsync(AiTestData.Request(), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCodes.ProviderNotConfigured, exception.ErrorCode);
        Assert.Equal(0, p1.CallCount);
    }

    [Fact]
    public async Task SuccessLogsStructuredFields()
    {
        RecordingAiChatClient p1 = RecordingAiChatClient.Succeed("p1");
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible).Register("p1", p1);

        QlxAiOptions options = AiTestData.Options(
            fixedProvider: "p1",
            providers: ProviderMap(("p1", 100)));

        (AiInvoker invoker, CapturingLogger<AiInvoker> logger) = Create(options, fakeProvider);

        await invoker.InvokeAsync(AiTestData.Request(), TestContext.Current.CancellationToken);

        CapturedLogEntry start = Entry(logger, 4101);
        Assert.Equal(LogLevel.Information, start.Level);
        Assert.Equal(QlxAiOptions.Strategies.Fixed, start.Field("Strategy"));
        Assert.Equal(1L, FieldLong(start, "MessageCount"));
        Assert.Equal(1L, FieldLong(start, "CandidateCount"));

        CapturedLogEntry success = Entry(logger, 4102);
        Assert.Equal("p1", success.Field("Provider"));
        Assert.True(FieldLong(success, "DurationMilliseconds") >= 0, "耗时字段应为非负");
        Assert.True(FieldLong(success, "ContentLength") > 0, "成功响应正文长度应大于 0");

        Assert.Equal(LogLevel.Information, Entry(logger, 4104).Level);
    }

    [Fact]
    public async Task AllFailureLogsStructuredFields()
    {
        RecordingAiChatClient p1 = RecordingAiChatClient.Fail("p1", new AiProviderException("p1", "HTTP_500", "boom", 500));
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible).Register("p1", p1);

        QlxAiOptions options = AiTestData.Options(
            fixedProvider: "p1",
            providers: ProviderMap(("p1", 100)));

        (AiInvoker invoker, CapturingLogger<AiInvoker> logger) = Create(options, fakeProvider);

        await Assert.ThrowsAsync<AiInvocationException>(
            () => invoker.InvokeAsync(AiTestData.Request(), TestContext.Current.CancellationToken));

        CapturedLogEntry failed = Entry(logger, 4103);
        Assert.Equal(LogLevel.Warning, failed.Level);
        Assert.Equal("p1", failed.Field("Provider"));
        Assert.Equal("HTTP_500", failed.Field("ErrorCode"));
        Assert.Equal("500", failed.Field("HttpStatusCode"));
        Assert.Equal("Failed", failed.Field("Outcome"));
        Assert.True(FieldLong(failed, "DurationMilliseconds") >= 0, "耗时字段应为非负");

        CapturedLogEntry summary = Entry(logger, 4105);
        Assert.Equal(LogLevel.Error, summary.Level);
        Assert.Contains("HTTP_500", summary.Field("ErrorCodes"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiKeyNeverAppearsInLogs()
    {
        RecordingAiChatClient successClient = RecordingAiChatClient.Succeed("p1");
        FakeAiChatClientProvider successProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible)
            .Register("p1", successClient);
        QlxAiOptions successOptions = AiTestData.Options(
            fixedProvider: "p1",
            providers: new Dictionary<string, AiProviderOptions>
            {
                ["p1"] = new() { ApiKey = SecretApiKey },
            });

        (AiInvoker successInvoker, CapturingLogger<AiInvoker> successLogger) = Create(successOptions, successProvider);
        AiChatResponse response = await successInvoker.InvokeAsync(AiTestData.Request(), TestContext.Current.CancellationToken);
        Assert.Equal("p1", response.ProviderName);
        AssertSecretNotLogged(successLogger);

        RecordingAiChatClient failingClient = RecordingAiChatClient.Fail(
            "p1", new AiProviderException("p1", "HTTP_500", "boom", 500));
        FakeAiChatClientProvider failingProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible)
            .Register("p1", failingClient);
        QlxAiOptions failingOptions = AiTestData.Options(
            fixedProvider: "p1",
            providers: new Dictionary<string, AiProviderOptions>
            {
                ["p1"] = new() { ApiKey = SecretApiKey },
            });

        (AiInvoker failingInvoker, CapturingLogger<AiInvoker> failingLogger) = Create(failingOptions, failingProvider);
        await Assert.ThrowsAsync<AiInvocationException>(
            () => failingInvoker.InvokeAsync(AiTestData.Request(), TestContext.Current.CancellationToken));
        AssertSecretNotLogged(failingLogger);

        AiProviderOptions providerOptions = new() { ApiKey = SecretApiKey };
        Assert.DoesNotContain(SecretApiKey, providerOptions.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestContentIsNotLoggedByDefault()
    {
        RecordingAiChatClient p1 = RecordingAiChatClient.Succeed("p1");
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible).Register("p1", p1);

        QlxAiOptions options = AiTestData.Options(
            fixedProvider: "p1",
            providers: ProviderMap(("p1", 100)));

        (AiInvoker invoker, CapturingLogger<AiInvoker> logger) = Create(options, fakeProvider);

        await invoker.InvokeAsync(AiTestData.Request(), TestContext.Current.CancellationToken);

        CapturedLogEntry start = Entry(logger, 4101);
        Assert.Equal(string.Empty, start.Field("ContentPreview"));
        Assert.DoesNotContain("你好", start.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestContentPreviewIsTruncatedWhenEnabled()
    {
        RecordingAiChatClient p1 = RecordingAiChatClient.Succeed("p1");
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible).Register("p1", p1);

        QlxAiOptions options = new()
        {
            Enabled = true,
            Strategy = QlxAiOptions.Strategies.Fixed,
            FixedProvider = "p1",
            LogRequestContent = true,
            MaxLogContentLength = 5,
            Providers = ProviderMap(("p1", 100)),
        };

        (AiInvoker invoker, CapturingLogger<AiInvoker> logger) = Create(options, fakeProvider);

        await invoker.InvokeAsync(AiTestData.Request(content: "你好"), TestContext.Current.CancellationToken);

        Assert.Equal("User:", Entry(logger, 4101).Field("ContentPreview"));
    }

    [Fact]
    public async Task CustomVendorTypeIsSupported()
    {
        const string CustomProviderType = "my-custom-vendor";
        RecordingAiChatClient custom = RecordingAiChatClient.Succeed("custom-vendor");
        FakeAiChatClientProvider fakeProvider = new FakeAiChatClientProvider(CustomProviderType)
            .Register("custom-vendor", custom);

        QlxAiOptions options = AiTestData.Options(
            fixedProvider: "custom-vendor",
            providers: new Dictionary<string, AiProviderOptions>
            {
                ["custom-vendor"] = new() { Type = CustomProviderType, Priority = 1 },
            });

        (AiInvoker invoker, _) = Create(options, fakeProvider);

        AiChatResponse response = await invoker.InvokeAsync(AiTestData.Request(), TestContext.Current.CancellationToken);

        Assert.Equal("custom-vendor", response.ProviderName);
        Assert.Equal(1, custom.CallCount);
        Assert.Equal("custom-vendor", Assert.Single(fakeProvider.CreatedProviderNames));
    }

    private static void AssertSecretNotLogged(CapturingLogger<AiInvoker> logger)
    {
        Assert.NotEmpty(logger.Entries);
        foreach (CapturedLogEntry entry in logger.Entries)
        {
            Assert.DoesNotContain(SecretApiKey, entry.Message, StringComparison.Ordinal);
            foreach (object? value in entry.Fields.Values)
            {
                Assert.DoesNotContain(SecretApiKey, value?.ToString() ?? string.Empty, StringComparison.Ordinal);
            }
        }
    }
}
