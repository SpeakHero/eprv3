/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 15:00:00 +08:00 中国标准时间 星期五
 * 描述：方法级审计过滤器 HTTP 集成测试——TestServer 真实 MVC 管线验证：
 *       成功动作写操作审计（含 TargetType / TargetId / 操作人 / 元数据）、
 *       异常动作写失败审计（:FAILED 后缀）且不留成功痕、未标注动作零审计
 */

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Web;

namespace Qlx.BuildingBlocks.Tests.Audit;

/// <summary>审计过滤器探测控制器（仅测试程序集内可见）。</summary>
[ApiController]
[Route("api/test/audit-probe")]
public sealed class AuditProbeController : ControllerBase
{
    /// <summary>成功动作：标注审计并返回 200。</summary>
    [HttpPost("ok/{id}")]
    [AuditOperation("support", "PROBE_OK", "执行了成功操作", TargetType = "Probe", TargetIdParam = "id")]
    public IActionResult OkAction(long id) => Ok(new { id });

    /// <summary>失败动作：标注审计并抛出未捕获异常（经异常中间件包装为 500）。</summary>
    [HttpPost("fail")]
    [AuditOperation("support", "PROBE_FAIL", "执行了将失败的操作")]
    public IActionResult FailAction()
        => throw new InvalidOperationException($"{ControllerContext.ActionDescriptor.DisplayName} 探测性失败");

    /// <summary>未标注动作：不应产生任何审计。</summary>
    [HttpGet("plain")]
    public IActionResult PlainAction() => Ok();
}

/// <summary>
/// <see cref="OperationAuditFilter"/> 集成测试（TestServer + InMemoryAuditService）。
/// </summary>
public sealed class OperationAuditFilterTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;

    public OperationAuditFilterTests()
    {
        var builder = Host.CreateDefaultBuilder();
        builder.ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.UseTestServer();
            webBuilder.ConfigureServices(services =>
            {
                services.AddSingleton<InMemoryAuditService>();
                services.AddSingleton<Qlx.Abstractions.Audit.IAuditService>(static sp => sp.GetRequiredService<InMemoryAuditService>());
                services.AddSingleton<ICurrentUserContext>(new TestCurrentUserContext("77"));
                services.AddSingleton<ITenantContext>(new TestTenantContext(9L));
                services.AddControllers()
                    .AddApplicationPart(typeof(AuditProbeController).Assembly)
                    .AddQlxOperationAudit();
            });
            webBuilder.Configure(app =>
            {
                app.UseExceptionHandlerMiddleware();
                app.UseRouting();
                app.UseEndpoints(static endpoints => endpoints.MapControllers());
            });
        });

        _host = builder.Build();
        _host.Start();
        _client = _host.GetTestClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    private InMemoryAuditService AuditService => _host.Services.GetRequiredService<InMemoryAuditService>();

    [Fact]
    public async Task SuccessActionWritesOperationAuditWithTargetAndMetadata()
    {
        var response = await _client.PostAsync("/api/test/audit-probe/ok/42", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var operations = AuditService.GetCapturedOperations();
        var entry = Assert.Single(operations);
        Assert.Equal("support", entry.ModuleCode);
        Assert.Equal("PROBE_OK", entry.Operation);
        Assert.Equal("执行了成功操作", entry.Description);
        Assert.Equal("77", entry.OperatedBy);
        Assert.Equal(9L, entry.TenantId);
        Assert.Equal("Probe", entry.TargetType);
        Assert.Equal(42L, entry.TargetId);
        Assert.NotNull(entry.Metadata);
        // TestServer 回环请求无 RemoteIpAddress，ip 为 null 被全局 WhenWritingNull 省略，断言其余元数据键
        Assert.Contains("\"method\":", entry.Metadata);
        Assert.Contains("\"path\":", entry.Metadata);
    }

    [Fact]
    public async Task FailedActionWritesFailedAuditAndNeverSuccessAudit()
    {
        var response = await _client.PostAsync("/api/test/audit-probe/fail", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var operations = AuditService.GetCapturedOperations();
        var entry = Assert.Single(operations);
        Assert.Equal("PROBE_FAIL:FAILED", entry.Operation);
        Assert.Equal(OperationAuditFilter.FailedOperationSuffix, entry.Operation["PROBE_FAIL".Length..]);
        Assert.Contains("失败", entry.Description);
        // 绝不出现成功痕
        Assert.DoesNotContain(operations, static o => o.Operation == "PROBE_FAIL");
    }

    [Fact]
    public async Task UnannotatedActionWritesNoAudit()
    {
        var response = await _client.GetAsync("/api/test/audit-probe/plain", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(AuditService.GetCapturedOperations());
    }
}
