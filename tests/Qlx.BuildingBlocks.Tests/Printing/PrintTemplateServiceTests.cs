/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 15:00:00 +08:00 中国标准时间 星期五
 * 描述：打印模板服务单元测试——CRUD 与版本自增、Scriban 数据占位渲染 + CSS 并入、
 *       qrcode 占位符渲染为内嵌 base64 PNG、启用态门禁（草稿/停用不承接渲染，缓存失效自愈）
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Printing;
using Qlx.BuildingBlocks.Caching;
using Qlx.Modules.Support;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Options;
using Qlx.Modules.Support.Services.Printing;

namespace Qlx.BuildingBlocks.Tests.Printing;

/// <summary>
/// <see cref="PrintTemplateService"/> 单元测试（InMemory SupportTenantDbContext + 真实 MemoryCacheService）。
/// </summary>
public sealed class PrintTemplateServiceTests
{
    private const long TenantId = 7L;
    private const string TemplateCode = "test.order.print";

    private static (PrintTemplateService Service, SupportTenantDbContext Db) Build()
    {
        var db = new SupportTenantDbContext(
            new Qlx.BuildingBlocks.Tests.Jobs.JobStubTenantDatabaseResolver(),
            TenantId,
            new DbContextOptionsBuilder<SupportTenantDbContext>()
                .UseInMemoryDatabase($"prt-{Guid.NewGuid():N}")
                .ConfigureWarnings(static w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
        var cache = new MemoryCacheService(Options.Create(new CacheOptions()));
        var service = new PrintTemplateService(db, cache, Options.Create(new SupportPrintOptions()));
        return (service, db);
    }

    private static PrintTemplateCreateRequest CreateRequest(
        string html = "订单 {{order_no}} 数量 {{qty}} {{qrcode:XS-001}}", string? css = ".t{color:red}")
        => new(TemplateCode, "测试打印模板", "test.order", html, css);

    private static Dictionary<string, object?> Variables()
        => new(StringComparer.Ordinal) { ["order_no"] = "XS-001", ["qty"] = 3L };

    // ==================== CRUD 与版本 ====================

    [Fact]
    public async Task CreateAsyncThenGetByCodeRoundTripWithDraftStatus()
    {
        var (service, _) = Build();

        var created = await service.CreateAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(PrintTemplateStatus.Draft, created.Status);
        Assert.Equal(1, created.Version);
        Assert.Equal("test.order", created.BizType);

        var view = await service.GetByCodeAsync(TemplateCode, TestContext.Current.CancellationToken);
        Assert.NotNull(view);
        Assert.Equal(created.Id, view!.Id);

        // 2A 冻结契约定义查询：ModuleCode 由 BizType 承载
        var info = await service.GetTemplateAsync(TemplateCode, TestContext.Current.CancellationToken);
        Assert.NotNull(info);
        Assert.Equal("test.order", info!.ModuleCode);
        Assert.Equal(TemplateCode, info.TemplateCode);
    }

    [Fact]
    public async Task CreateAsyncDuplicateCodeThrowsConflict()
    {
        var (service, _) = Build();
        await service.CreateAsync(CreateRequest(), TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync(CreateRequest(), TestContext.Current.CancellationToken));
        Assert.Equal("PRINT_TEMPLATE_DUPLICATED", exception.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsyncBumpsVersionAndKeepsStatus()
    {
        var (service, _) = Build();
        var created = await service.CreateAsync(CreateRequest(), TestContext.Current.CancellationToken);

        var updated = await service.UpdateAsync(created.Id,
            new PrintTemplateUpdateRequest("新名称", "test.order", "v2 内容", null),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, updated.Version);
        Assert.Equal("新名称", updated.Name);
        Assert.Equal(PrintTemplateStatus.Draft, updated.Status);

        var view = await service.GetByCodeAsync(TemplateCode, TestContext.Current.CancellationToken);
        Assert.NotNull(view);
        Assert.Equal(2, view!.Version);
        Assert.Equal("v2 内容", view.ContentHtml);
    }

    [Fact]
    public async Task GetTemplateAsyncUnknownTemplateReturnsNull()
    {
        var (service, _) = Build();

        var info = await service.GetTemplateAsync("none.print", TestContext.Current.CancellationToken);

        Assert.Null(info);
    }

    // ==================== 渲染管线 ====================

    [Fact]
    public async Task RenderAsyncReplacesDataPlaceholdersQrCodeAndPrependsCss()
    {
        var (service, _) = Build();
        var created = await service.CreateAsync(CreateRequest(), TestContext.Current.CancellationToken);
        await service.SetStatusAsync(created.Id, PrintTemplateStatus.Enabled, TestContext.Current.CancellationToken);

        var html = await service.RenderAsync(TemplateCode, Variables(), TestContext.Current.CancellationToken);

        Assert.StartsWith("<style>.t{color:red}</style>", html);
        Assert.Contains("订单 XS-001 数量 3", html);
        Assert.Contains("<img src=\"data:image/png;base64,", html);
        Assert.Contains("alt=\"qrcode\"", html);
    }

    [Fact]
    public async Task RenderAsyncDraftTemplateThrowsNotFound()
    {
        var (service, _) = Build();
        await service.CreateAsync(CreateRequest(), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.RenderAsync(TemplateCode, Variables(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RenderAsyncAfterDisableThrowsNotFoundWithCacheInvalidated()
    {
        var (service, _) = Build();
        var created = await service.CreateAsync(CreateRequest(), TestContext.Current.CancellationToken);
        await service.SetStatusAsync(created.Id, PrintTemplateStatus.Enabled, TestContext.Current.CancellationToken);
        await service.RenderAsync(TemplateCode, Variables(), TestContext.Current.CancellationToken);

        await service.SetStatusAsync(created.Id, PrintTemplateStatus.Disabled, TestContext.Current.CancellationToken);

        // 停用后缓存失效（读穿不返回旧启用态模板）
        await Assert.ThrowsAsync<NotFoundException>(
            () => service.RenderAsync(TemplateCode, Variables(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RenderAsyncQrCodeResolvesModelFieldWhenPresent()
    {
        var (service, _) = Build();
        var created = await service.CreateAsync(new PrintTemplateCreateRequest(
            TemplateCode, "溯源模板", "test.order", "{{qrcode:trace_code}}", null), TestContext.Current.CancellationToken);
        await service.SetStatusAsync(created.Id, PrintTemplateStatus.Enabled, TestContext.Current.CancellationToken);

        var html = await service.RenderAsync(TemplateCode,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["trace_code"] = "TRC-777" },
            TestContext.Current.CancellationToken);

        // 二维码内容优先解析模型字段（渲染 base64 PNG，等值性由扫码验证，此处断言 img 节点生成）
        Assert.DoesNotContain("{{qrcode:", html);
        Assert.Contains("<img src=\"data:image/png;base64,", html);
    }
}
