/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22
 * 描述：Id 混淆 HTTP 集成测试——通过 TestServer + HttpClient 验证真实 HTTP 管线下的两种绑定方式：
 *       1) JSON Body：Id 命名属性由 QlxIdJsonMetadata 约定自动混淆/反混淆
 *       2) 路由/查询：long / long? 由 HashidsLongModelBinder 自动解码
 *       覆盖正常、异常、边界值场景，确认全局约定不影响非 Id long 字段。
 */

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.Web;

namespace Qlx.BuildingBlocks.Tests.Web;

public sealed class IdObfuscationHttpTests : IDisposable
{
    // 与其他 Id 测试共用同一盐值：IIdObfuscatorRegistry 为进程级静态状态
    private const string TestSalt = "unit-test-global-id-salt";

    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly HashidsObfuscator _obfuscator;

    public IdObfuscationHttpTests()
    {
        _obfuscator = new HashidsObfuscator(TestSalt, minLength: 8);
        IIdObfuscatorRegistry.Register(_obfuscator);

        var builder = Host.CreateDefaultBuilder();
        builder.ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.UseTestServer();
            webBuilder.ConfigureServices(services =>
            {
                services.AddSingleton<IIdObfuscator>(_obfuscator);
                services.AddControllers()
                    .AddApplicationPart(typeof(IdObfuscationTestController).Assembly)
                    .AddQlxJsonSerialization()
                    .AddQlxIdModelBinding();
            });
            webBuilder.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
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

    // ==================== JSON Body 绑定（QlxIdJsonMetadata 约定） ====================

    [Fact]
    public async Task PostBodyObfuscatedIdsDecodesAndReEncodes()
    {
        var payload = new { id = _obfuscator.Encode(42L), parentId = _obfuscator.Encode(7L) };

        var response = await _client.PostAsJsonAsync("/api/test/id-obfuscation/body", payload, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ParseJsonAsync(response);
        Assert.Equal(_obfuscator.Encode(42L), body.GetProperty("id").GetString());
        Assert.Equal(_obfuscator.Encode(7L), body.GetProperty("parentId").GetString());
    }

    [Fact]
    public async Task PostBodyPlainNumericIdsBackwardCompatible()
    {
        var payload = new { id = 100, parentId = 200 };

        var response = await _client.PostAsJsonAsync("/api/test/id-obfuscation/body", payload, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ParseJsonAsync(response);
        Assert.Equal(_obfuscator.Encode(100L), body.GetProperty("id").GetString());
        Assert.Equal(_obfuscator.Encode(200L), body.GetProperty("parentId").GetString());
    }

    [Fact]
    public async Task PostBodyInvalidIdReturnsBadRequest()
    {
        var payload = new { id = "!!!not-valid!!!", parentId = (long?)null };

        var response = await _client.PostAsJsonAsync("/api/test/id-obfuscation/body", payload, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostBodyNullParentIdOmitsPropertyByDefault()
    {
        var payload = new { id = _obfuscator.Encode(1L), parentId = (long?)null };

        var response = await _client.PostAsJsonAsync("/api/test/id-obfuscation/body", payload, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ParseJsonAsync(response);
        // 全局 DefaultIgnoreCondition.WhenWritingNull：null 的可空 Id 默认不序列化
        Assert.False(body.TryGetProperty("parentId", out _));
        Assert.Equal(_obfuscator.Encode(1L), body.GetProperty("id").GetString());
    }

    // ==================== 路由参数绑定（HashidsLongModelBinder） ====================

    [Fact]
    public async Task GetByRouteObfuscatedIdDecodesAndEchoesObfuscated()
    {
        var publicId = _obfuscator.Encode(42L);

        var response = await _client.GetAsync($"/api/test/id-obfuscation/route/{publicId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ParseJsonAsync(response);
        Assert.Equal(publicId, body.GetProperty("id").GetString());
    }

    [Fact]
    public async Task GetByRoutePlainNumericIdBackwardCompatible()
    {
        var response = await _client.GetAsync("/api/test/id-obfuscation/route/123456789", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ParseJsonAsync(response);
        Assert.Equal(_obfuscator.Encode(123456789L), body.GetProperty("id").GetString());
    }

    [Fact]
    public async Task GetByRouteInvalidIdReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/test/id-obfuscation/route/!!!invalid!!!", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ==================== 查询参数绑定 ====================

    [Fact]
    public async Task GetByQueryBothParamsDecodesCorrectly()
    {
        var orgId = _obfuscator.Encode(10L);
        var id = _obfuscator.Encode(20L);

        var response = await _client.GetAsync($"/api/test/id-obfuscation/query?orgId={orgId}&id={id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ParseJsonAsync(response);
        Assert.Equal(orgId, body.GetProperty("orgId").GetString());
        Assert.Equal(id, body.GetProperty("id").GetString());
    }

    [Fact]
    public async Task GetByQueryMissingNullableOrgIdOmitsProperty()
    {
        var id = _obfuscator.Encode(20L);

        var response = await _client.GetAsync($"/api/test/id-obfuscation/query?id={id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ParseJsonAsync(response);
        Assert.False(body.TryGetProperty("orgId", out _));
        Assert.Equal(id, body.GetProperty("id").GetString());
    }

    [Fact]
    public async Task GetByQueryMissingRequiredIdReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/test/id-obfuscation/query?orgId=" + _obfuscator.Encode(10L), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetByQueryInvalidIdReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/test/id-obfuscation/query?id=!!!bad!!!", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetByQueryBlankNullableOrgIdOmitsProperty()
    {
        var id = _obfuscator.Encode(20L);

        var response = await _client.GetAsync($"/api/test/id-obfuscation/query?orgId=&id={id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ParseJsonAsync(response);
        Assert.False(body.TryGetProperty("orgId", out _));
    }

    // ==================== 多段路由 ====================

    [Fact]
    public async Task GetByRouteMultiTwoObfuscatedIdsDecodesBoth()
    {
        var parentId = _obfuscator.Encode(1L);
        var childId = _obfuscator.Encode(2L);

        var response = await _client.GetAsync($"/api/test/id-obfuscation/route/{parentId}/child/{childId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ParseJsonAsync(response);
        Assert.Equal(parentId, body.GetProperty("parentId").GetString());
        Assert.Equal(childId, body.GetProperty("id").GetString());
    }

    // ==================== 嵌套对象 / 集合 ====================

    [Fact]
    public async Task PostNestedBodyEncodesIdsButKeepsNonIdLongNumeric()
    {
        var payload = new
        {
            id = _obfuscator.Encode(10L),
            total = 1234L,
            child = new
            {
                id = _obfuscator.Encode(20L),
                parentId = _obfuscator.Encode(30L),
                amount = 55L,
            },
        };

        var response = await _client.PostAsJsonAsync("/api/test/id-obfuscation/nested-body", payload, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ParseJsonAsync(response);
        Assert.Equal(_obfuscator.Encode(10L), body.GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Number, body.GetProperty("total").ValueKind);
        Assert.Equal(1234L, body.GetProperty("total").GetInt64());

        var child = body.GetProperty("child");
        Assert.Equal(_obfuscator.Encode(20L), child.GetProperty("id").GetString());
        Assert.Equal(_obfuscator.Encode(30L), child.GetProperty("parentId").GetString());
        Assert.Equal(JsonValueKind.Number, child.GetProperty("amount").ValueKind);
        Assert.Equal(55L, child.GetProperty("amount").GetInt64());
    }

    [Fact]
    public async Task PostListBodyEncodesEveryElementId()
    {
        var payload = new
        {
            items = new[]
            {
                new { id = _obfuscator.Encode(1L), parentId = (string?)_obfuscator.Encode(2L) },
                new { id = _obfuscator.Encode(3L), parentId = (string?)null },
            },
            total = 2L,
        };

        var response = await _client.PostAsJsonAsync("/api/test/id-obfuscation/list-body", payload, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ParseJsonAsync(response);
        var items = body.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal(_obfuscator.Encode(1L), items[0].GetProperty("id").GetString());
        Assert.Equal(_obfuscator.Encode(2L), items[0].GetProperty("parentId").GetString());
        Assert.Equal(_obfuscator.Encode(3L), items[1].GetProperty("id").GetString());
        Assert.False(items[1].TryGetProperty("parentId", out _));
        Assert.Equal(JsonValueKind.Number, body.GetProperty("total").ValueKind);
    }

    // ==================== 雪花精度 / 空值边界 ====================

    [Fact]
    public async Task GetByRouteSnowflakeScaleIdRoundTripsExactly()
    {
        // 超过 JS 安全整数上限（2^53-1）的典型雪花量级：必须以字符串原样往返
        const long snowflakeId = 1_900_000_000_000_000_123L;
        var publicId = _obfuscator.Encode(snowflakeId);

        var response = await _client.GetAsync($"/api/test/id-obfuscation/route/{publicId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ParseJsonAsync(response);
        Assert.Equal(publicId, body.GetProperty("id").GetString());
        Assert.Equal(snowflakeId, _obfuscator.TryDecode(body.GetProperty("id").GetString()!, out var decoded) ? decoded : 0L);
    }

    [Fact]
    public async Task PostBodyNullForNonNullableIdBindsAsZeroAndWritesNumber()
    {
        var payload = new { id = (long?)null, parentId = (long?)null };

        var response = await _client.PostAsJsonAsync("/api/test/id-obfuscation/body", payload, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ParseJsonAsync(response);
        // 非可空 Id 收到 null 时按 0 处理；0 不参与混淆，保持数值输出
        Assert.Equal(JsonValueKind.Number, body.GetProperty("id").ValueKind);
        Assert.Equal(0L, body.GetProperty("id").GetInt64());
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
        return document.RootElement.Clone();
    }
}
