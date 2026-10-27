/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-27 10:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-27 10:30:00 +08:00 中国标准时间 星期二
 * 描述：全局响应包装专项测试——TestServer 真实管线验证：已包装透传（防重复包装）、裸 JSON 兜底包装、
 *       模型校验 / JSON 反序列化失败 400 包装、422 业务错误透传、500 仍由异常处理器接管、SSE 豁免、非 /api 路由直通
 */

using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Qlx.Abstractions.Exceptions;
using Qlx.BuildingBlocks.Api;

namespace Qlx.BuildingBlocks.Tests.Api;

// 探测动作均为固定响应、不触实例成员，CA1822 建议改 static——但 MVC 不支持 static 动作方法
#pragma warning disable CA1822
/// <summary>响应包装探测控制器（仅测试程序集内可见）。</summary>
[ApiController]
[Route("api/test/wrap-probe")]
public sealed class WrapProbeController : ControllerBase
{
    /// <summary>已手动包装的成功响应 → 中间件应透传，不得重复包装。</summary>
    [HttpGet("ok")]
    public ApiResponse<object> OkProbe() => ApiResponse<object>.Ok(new { Name = "已包装" });

    /// <summary>裸 JSON 成功响应（模拟漏包装）→ 中间件应兜底包装为 success=true 信封。</summary>
    [HttpGet("bare")]
    public object BareProbe() => new { Name = "裸数据", Qty = 3 };

    /// <summary>模型校验入口：Name 必填；Qty 类型不匹配触发 JSON 反序列化失败。</summary>
    [HttpPost("validate")]
    public ApiResponse<object> ValidateProbe([FromBody] WrapProbePayload payload)
        => ApiResponse<object>.Ok(new { payload.Name, payload.Qty });

    /// <summary>业务校验异常 → 422（异常处理器输出，中间件应透传）。</summary>
    [HttpGet("validation")]
    public IActionResult ValidationProbe() => throw new Qlx.Abstractions.Exceptions.ValidationException([new FieldError("Name", "名称必填")]);

    /// <summary>未知异常 → 500（异常处理器领地，中间件不得介入）。</summary>
    [HttpGet("unknown")]
    public IActionResult UnknownProbe() => throw new InvalidOperationException("探测性未处理异常");

    /// <summary>SSE 豁免端点：标记 SkipApiResponseWrap，响应不得被缓冲 / 改写。</summary>
    [HttpGet("stream")]
    [SkipApiResponseWrap]
    public async Task StreamProbe()
    {
        Response.ContentType = "text/event-stream";
        await Response.WriteAsync("data: ping\n\n");
    }
}
#pragma warning restore CA1822

/// <summary>包装探测入参（Name 必填触发模型校验；Qty 类型非法触发 JSON 反序列化失败）。</summary>
public sealed record WrapProbePayload
{
    /// <summary>名称（必填）。</summary>
    [Required(ErrorMessage = "名称必填")]
    public string? Name { get; init; }

    /// <summary>数量。</summary>
    public int Qty { get; init; }
}

/// <summary>
/// <see cref="ApiResponseWrapperMiddleware"/> + <see cref="ApiResponseWrapperExtensions"/> 专项测试：
/// 统一响应契约 = 成功 / 400 / 422 / 500 均为 ApiResponse 信封，无遗漏场景、无重复包装。
/// </summary>
public sealed class ApiResponseWrapperMiddlewareTests
{
    // ======= 成功响应 =======

    [Fact]
    public async Task AlreadyWrappedSuccessPassesThroughWithoutDoubleWrapping()
    {
        using var result = await ProbeAsync(client => client.GetAsync(
            "/api/test/wrap-probe/ok", TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, result.Status);
        var root = result.Body.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("已包装", root.GetProperty("data").GetProperty("name").GetString());
        // 防重复包装：data 内不得再出现信封字段
        Assert.False(root.GetProperty("data").TryGetProperty("success", out _));
    }

    [Fact]
    public async Task BareJsonSuccessGetsWrappedAsFallback()
    {
        using var result = await ProbeAsync(client => client.GetAsync(
            "/api/test/wrap-probe/bare", TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, result.Status);
        var root = result.Body.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("裸数据", root.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal(3, root.GetProperty("data").GetProperty("qty").GetInt32());
    }

    // ======= 400：模型校验 / JSON 反序列化 =======

    [Fact]
    public async Task ModelValidationFailureMapsTo400Envelope()
    {
        using var result = await ProbeAsync(client => client.PostAsync(
            "/api/test/wrap-probe/validate",
            new StringContent("{\"qty\":1}", Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
        var root = result.Body.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("VALIDATION_ERROR", root.GetProperty("code").GetString());
        Assert.Equal("请求数据校验失败", root.GetProperty("message").GetString());
        var error = root.GetProperty("errors").EnumerateArray().Single();
        Assert.Equal("Name", error.GetProperty("fieldName").GetString());
        Assert.Equal("名称必填", error.GetProperty("errorMessage").GetString());
    }

    [Fact]
    public async Task JsonDeserializationFailureMapsTo400Envelope()
    {
        using var result = await ProbeAsync(client => client.PostAsync(
            "/api/test/wrap-probe/validate",
            new StringContent("{\"name\":\"x\",\"qty\":\"abc\"}", Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
        var root = result.Body.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("VALIDATION_ERROR", root.GetProperty("code").GetString());
        // JSON 解析失败消息归一为友好文案，不暴露框架内部细节
        Assert.DoesNotContain("could not be converted", result.Raw);
        Assert.Contains(root.GetProperty("errors").EnumerateArray(),
            error => error.GetProperty("errorMessage").GetString() == "请求体 JSON 格式或字段类型非法");
    }

    // ======= 422：业务校验（异常处理器输出，透传） =======

    [Fact]
    public async Task BusinessValidationErrorKeeps422EnvelopeFromExceptionHandler()
    {
        using var result = await ProbeAsync(client => client.GetAsync(
            "/api/test/wrap-probe/validation", TestContext.Current.CancellationToken));

        Assert.Equal((HttpStatusCode)422, result.Status);
        var root = result.Body.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("VALIDATION_ERROR", root.GetProperty("code").GetString());
        var error = root.GetProperty("errors").EnumerateArray().Single();
        Assert.Equal("Name", error.GetProperty("fieldName").GetString());
        Assert.Equal("名称必填", error.GetProperty("errorMessage").GetString());
    }

    // ======= 500：异常处理器领地，中间件不介入 =======

    [Fact]
    public async Task UnknownExceptionStillHandledByExceptionHandler()
    {
        using var result = await ProbeAsync(client => client.GetAsync(
            "/api/test/wrap-probe/unknown", TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.InternalServerError, result.Status);
        var root = result.Body.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("INTERNAL_ERROR", root.GetProperty("code").GetString());
        Assert.Contains("服务器内部错误", root.GetProperty("message").GetString());
    }

    // ======= 豁免与直通 =======

    [Fact]
    public async Task SseEndpointWithSkipAttributePassesThroughUntouched()
    {
        using var result = await ProbeAsync(client => client.GetAsync(
            "/api/test/wrap-probe/stream", TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.StartsWith("text/event-stream", result.ContentType);
        Assert.Equal("data: ping\n\n", result.Raw);
    }

    [Fact]
    public async Task NonApiRoutePassesThroughUntouched()
    {
        using var result = await ProbeAsync(client => client.GetAsync(
            "/outside-api/bare", TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal("{\"name\":\"裸露\"}", result.Raw);
    }

    // ======= 基建 =======

    private static IHost CreateHost()
    {
        var builder = Host.CreateDefaultBuilder();
        builder.ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.UseEnvironment(Environments.Development);
            webBuilder.UseTestServer();
            webBuilder.ConfigureServices(services =>
            {
                services.AddQlxExceptionHandling();
                services.AddControllers()
                    .AddApplicationPart(typeof(WrapProbeController).Assembly)
                    .AddQlxApiResponseWrapping();
            });
            webBuilder.Configure(app =>
            {
                app.UseQlxExceptionHandling();
                app.UseRouting();
                app.UseQlxApiResponseWrapping();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                    endpoints.MapGet("/outside-api/bare", async context =>
                    {
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync("{\"name\":\"裸露\"}");
                    });
                });
            });
        });

        var host = builder.Build();
        host.Start();
        return host;
    }

    private static async Task<ProbeResult> ProbeAsync(Func<HttpClient, Task<HttpResponseMessage>> probe)
    {
        using var host = CreateHost();
        var client = host.GetTestClient();
        var response = await probe(client).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        return new ProbeResult(
            response.StatusCode,
            response.Content.Headers.ContentType?.ToString() ?? string.Empty,
            raw);
    }

    private sealed class ProbeResult(HttpStatusCode status, string contentType, string raw) : IDisposable
    {
        private readonly JsonDocument? _body = raw.Length > 0 && raw.TrimStart().StartsWith('{') ? JsonDocument.Parse(raw) : null;

        public HttpStatusCode Status { get; } = status;

        public string ContentType { get; } = contentType;

        public string Raw { get; } = raw;

        public JsonDocument Body => _body ?? throw new InvalidOperationException("响体不是 JSON 对象");

        public void Dispose() => _body?.Dispose();
    }
}
