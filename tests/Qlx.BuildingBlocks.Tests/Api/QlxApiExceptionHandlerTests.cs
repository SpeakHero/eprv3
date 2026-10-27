/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 12:00:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 12:00:00 +08:00 中国标准时间 星期一
 * 描述：统一异常处理器专项测试——TestServer 真实管线验证各异常类型→状态码映射、
 *       ApiResponse 信封、500 详情环境隔离；DefaultHttpContext 直测客户端断开静默与全量接管
 */

using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using Qlx.Abstractions.Exceptions;
using Qlx.BuildingBlocks.Api;

namespace Qlx.BuildingBlocks.Tests.Api;

// 探测动作均为纯抛异常、不触实例成员，CA1822 建议改 static——但 MVC 不支持 static 动作方法
#pragma warning disable CA1822
/// <summary>异常处理器探测控制器（仅测试程序集内可见）。</summary>
[ApiController]
[Route("api/test/exception-probe")]
public sealed class ExceptionProbeController : ControllerBase
{
    /// <summary>资源不存在 → 404。</summary>
    [HttpGet("not-found")]
    public IActionResult NotFoundProbe() => throw new NotFoundException("采购单", "42");

    /// <summary>资源冲突 → 409，携带自定义错误码。</summary>
    [HttpGet("conflict")]
    public IActionResult ConflictProbe() => throw new ConflictException("单据已停用", "PROBE_CONFLICT");

    /// <summary>字段校验失败 → 422，携带字段级错误列表。</summary>
    [HttpGet("validation")]
    public IActionResult ValidationProbe() => throw new ValidationException([new FieldError("Name", "名称必填")]);

    /// <summary>未认证 → 401。</summary>
    [HttpGet("unauthorized")]
    public IActionResult UnauthorizedProbe() => throw new UnauthorizedException();

    /// <summary>无权限 → 403。</summary>
    [HttpGet("forbidden")]
    public IActionResult ForbiddenProbe() => throw new ForbiddenException();

    /// <summary>业务异常泛化分支（非具体子类）→ 422。</summary>
    [HttpGet("business")]
    public IActionResult BusinessProbe() => throw new ProbeBusinessException();

    /// <summary>请求体数据非法 → 400，不暴露框架细节。</summary>
    [HttpGet("invalid-data")]
    public IActionResult InvalidDataProbe() => throw new InvalidDataException("表单键超长");

    /// <summary>未知异常 → 500。</summary>
    [HttpGet("unknown")]
    public IActionResult UnknownProbe() => throw new InvalidOperationException("探测性未处理异常");
}
#pragma warning restore CA1822

/// <summary>探测用业务异常子类（命中 <see cref="BusinessException"/> 泛化分支而非具体类型分支）。</summary>
public sealed class ProbeBusinessException() : BusinessException("PROBE_BIZ", "探测性业务异常");

/// <summary>
/// <see cref="QlxApiExceptionHandler"/> 专项测试：
/// 集成（TestServer）覆盖状态码映射与信封结构；单元（DefaultHttpContext）覆盖客户端断开静默与全量接管。
/// </summary>
public sealed class QlxApiExceptionHandlerTests
{
    // ======= 集成：异常类型 → 状态码映射 =======

    [Fact]
    public async Task NotFoundExceptionMapsTo404WithEnvelope()
    {
        using var result = await ProbeAsync(Environments.Production, "not-found");

        Assert.Equal(HttpStatusCode.NotFound, result.Status);
        Assert.Equal("application/json; charset=utf-8", result.ContentType);
        Assert.False(result.Body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("NOT_FOUND", result.Body.RootElement.GetProperty("code").GetString());
        Assert.Contains("采购单", result.Body.RootElement.GetProperty("message").GetString());
        Assert.Contains("42", result.Body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ConflictExceptionMapsTo409WithCustomErrorCode()
    {
        using var result = await ProbeAsync(Environments.Production, "conflict");

        Assert.Equal(HttpStatusCode.Conflict, result.Status);
        Assert.Equal("PROBE_CONFLICT", result.Body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ValidationExceptionMapsTo422WithFieldErrors()
    {
        using var result = await ProbeAsync(Environments.Production, "validation");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, result.Status);
        var root = result.Body.RootElement;
        Assert.Equal("VALIDATION_ERROR", root.GetProperty("code").GetString());
        var errors = root.GetProperty("errors");
        Assert.Equal(1, errors.GetArrayLength());
        var first = errors[0];
        Assert.Equal("Name", first.GetProperty("fieldName").GetString());
        Assert.Equal("名称必填", first.GetProperty("errorMessage").GetString());
    }

    [Fact]
    public async Task UnauthorizedExceptionMapsTo401()
    {
        using var result = await ProbeAsync(Environments.Production, "unauthorized");

        Assert.Equal(HttpStatusCode.Unauthorized, result.Status);
        Assert.Equal("UNAUTHORIZED", result.Body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ForbiddenExceptionMapsTo403()
    {
        using var result = await ProbeAsync(Environments.Production, "forbidden");

        Assert.Equal(HttpStatusCode.Forbidden, result.Status);
        Assert.Equal("FORBIDDEN", result.Body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GenericBusinessExceptionMapsTo422()
    {
        using var result = await ProbeAsync(Environments.Production, "business");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, result.Status);
        Assert.Equal("PROBE_BIZ", result.Body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task InvalidDataExceptionMapsTo400WithoutFrameworkDetail()
    {
        using var result = await ProbeAsync(Environments.Production, "invalid-data");

        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
        var root = result.Body.RootElement;
        Assert.Equal("BAD_REQUEST", root.GetProperty("code").GetString());
        // 客户端错误不暴露框架内部限制细节
        Assert.Equal("请求格式非法或超出大小限制", root.GetProperty("message").GetString());
        Assert.DoesNotContain("表单键超长", result.Raw);
    }

    // ======= 集成：500 详情环境隔离 =======

    [Fact]
    public async Task UnknownExceptionMapsTo500AndHidesDetailInProduction()
    {
        using var result = await ProbeAsync(Environments.Production, "unknown");

        Assert.Equal(HttpStatusCode.InternalServerError, result.Status);
        var root = result.Body.RootElement;
        Assert.Equal("INTERNAL_ERROR", root.GetProperty("code").GetString());
        Assert.Equal("服务器内部错误", root.GetProperty("message").GetString());
        Assert.DoesNotContain("探测性未处理异常", result.Raw);
    }

    [Fact]
    public async Task UnknownExceptionRevealsDetailInDevelopment()
    {
        using var result = await ProbeAsync(Environments.Development, "unknown");

        Assert.Equal(HttpStatusCode.InternalServerError, result.Status);
        var message = result.Body.RootElement.GetProperty("message").GetString();
        Assert.Contains("服务器内部错误：", message);
        Assert.Contains("探测性未处理异常", message);
    }

    // ======= 单元：客户端断开静默与全量接管 =======

    [Fact]
    public async Task TryHandleAsyncSwallowsAbortedRequestCancellation()
    {
        var handler = CreateHandler(Environments.Production);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var context = new DefaultHttpContext { RequestAborted = cts.Token };
        using var body = new MemoryStream();
        context.Response.Body = body;

        var handled = await handler.TryHandleAsync(
            context, new OperationCanceledException(cts.Token), TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Equal(0, body.Length);
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task InternalCancellationNotCausedByClientStillMapsTo500()
    {
        var handler = CreateHandler(Environments.Production);
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;

        var handled = await handler.TryHandleAsync(
            context, new TaskCanceledException("内部超时"), TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Equal(500, context.Response.StatusCode);
        Assert.True(body.Length > 0);
    }

    [Fact]
    public async Task TryHandleAsyncTakesOverUnknownExceptionFully()
    {
        var handler = CreateHandler(Environments.Production);
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;

        var handled = await handler.TryHandleAsync(
            context, new InvalidOperationException("接管验证"), TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Equal(500, context.Response.StatusCode);
        var json = JsonSerializer.Deserialize<JsonElement>(body.ToArray().AsSpan());
        Assert.Equal("INTERNAL_ERROR", json.GetProperty("code").GetString());
    }

    // ======= 基建 =======

    private static QlxApiExceptionHandler CreateHandler(string environmentName)
        => new(new TestHostEnvironment(environmentName), NullLogger<QlxApiExceptionHandler>.Instance);

    private static IHost CreateHost(string environmentName)
    {
        var builder = Host.CreateDefaultBuilder();
        builder.ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.UseEnvironment(environmentName);
            webBuilder.UseTestServer();
            webBuilder.ConfigureServices(services =>
            {
                services.AddQlxExceptionHandling();
                services.AddControllers()
                    .AddApplicationPart(typeof(ExceptionProbeController).Assembly);
            });
            webBuilder.Configure(app =>
            {
                app.UseQlxExceptionHandling();
                app.UseRouting();
                app.UseEndpoints(static endpoints => endpoints.MapControllers());
            });
        });

        var host = builder.Build();
        host.Start();
        return host;
    }

    private static async Task<ProbeResult> ProbeAsync(string environmentName, string route)
    {
        using var host = CreateHost(environmentName);
        var client = host.GetTestClient();
        var response = await client.GetAsync(
            $"/api/test/exception-probe/{route}", TestContext.Current.CancellationToken).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        return new ProbeResult(
            response.StatusCode,
            response.Content.Headers.ContentType?.ToString() ?? string.Empty,
            raw,
            JsonDocument.Parse(raw));
    }

    private sealed record ProbeResult(HttpStatusCode Status, string ContentType, string Raw, JsonDocument Body) : IDisposable
    {
        public void Dispose() => Body.Dispose();
    }

    /// <summary>测试用宿主环境（仅控制 EnvironmentName）。</summary>
    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Qlx.BuildingBlocks.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
