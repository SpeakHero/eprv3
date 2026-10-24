/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 23:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 23:30:00 +08:00 中国标准时间 星期四
 * 描述：PortalManifestSyncService 单元测试：权限点 Upsert / 软删 / 恢复 + Guard 拦截器旁路验证
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Qlx.Abstractions.Modules;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.Modules;
using Qlx.Modules.Portal;
using Qlx.Modules.Portal.Services;
using AbstractionsPermDef = Qlx.Abstractions.Modules.PermissionDefinition;
using PortalPermDef = Qlx.Modules.Portal.Domain.Tenancy.PermissionDefinition;

namespace Qlx.BuildingBlocks.Tests.Modules;

/// <summary>桩雪花 Id 生成器（单调递增，便于断言）。</summary>
internal sealed class StubSnowflakeIdGenerator : ISnowflakeIdGenerator
{
    private long _current = 800000000000000000L;

    public long NextId() => Interlocked.Increment(ref _current);
}

/// <summary>桩 IModule：持有不可变的 ModuleManifest。</summary>
internal sealed class StubModule : IModule
{
    public ModuleManifest Manifest { get; }

    public StubModule(ModuleManifest manifest)
    {
        Manifest = manifest;
    }

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // 桩模块不注册服务
    }
}

/// <summary>
/// 基于 InMemory 数据库的 PortalDbContext 工厂桩。
/// 注入 ManifestDefinitionReadOnlyGuardInterceptor 以验证旁路场景。
/// </summary>
internal sealed class InMemoryPortalDbContextFactory : IDbContextFactory<PortalDbContext>
{
    private readonly string _databaseName;

    public InMemoryPortalDbContextFactory(string databaseName)
    {
        _databaseName = databaseName;
    }

    public PortalDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .AddInterceptors(new ManifestDefinitionReadOnlyGuardInterceptor())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new PortalDbContext(options);
    }
}

/// <summary>
/// PortalManifestSyncService 权限点同步核心逻辑测试。
/// 使用同名 InMemory 数据库让 SyncService 内部创建的 DbContext 与外部断言上下文共享数据。
/// </summary>
public sealed class ManifestSyncServiceTests
{
    private readonly string _dbName = $"manifest-sync-{Guid.NewGuid():N}";
    private readonly InMemoryPortalDbContextFactory _factory;

    public ManifestSyncServiceTests()
    {
        _factory = new InMemoryPortalDbContextFactory(_dbName);
    }

    // ==================== Upsert 新增场景 ====================

    [Fact]
    public async Task SyncModuleAsyncUpsertsNewModuleManifestPermissions()
    {
        // Arrange: 构造一个 Manifest 含 3 个权限点的模块
        var manifest = new ModuleManifest
        {
            ModuleCode = "pur",
            DisplayName = "采购管理",
            TablePrefix = "pur_",
            Permissions =
            [
                new AbstractionsPermDef("pur.order.view", "查看采购订单", PermissionKind.Menu),
                new AbstractionsPermDef("pur.order.edit", "编辑采购订单", PermissionKind.Action),
                new AbstractionsPermDef("pur.order.approve", "审批采购订单", PermissionKind.Action),
            ],
        };
        var module = new StubModule(manifest);
        var service = CreateService([module]);

        // Act
        await service.SyncModuleAsync(module, CancellationToken.None);

        // Assert
        using var db = _factory.CreateDbContext();
        var all = await db.PermissionDefinitions
            .Where(p => p.ModuleCode == "pur")
            .ToListAsync(CancellationToken.None);

        Assert.Equal(3, all.Count);
        Assert.All(all, p => Assert.False(p.IsDeprecated));
        Assert.Contains(all, p => p.PermissionCode == "pur.order.view");
        Assert.Contains(all, p => p.PermissionCode == "pur.order.edit");
        Assert.Contains(all, p => p.PermissionCode == "pur.order.approve");
    }

    // ==================== 软删场景 ====================

    [Fact]
    public async Task SyncModuleAsyncManifestRemovesOldPermissionMarksIsDeprecatedTrue()
    {
        // Arrange: 先同步一个含 3 个权限点的模块
        var manifest1 = new ModuleManifest
        {
            ModuleCode = "pur",
            DisplayName = "采购管理",
            TablePrefix = "pur_",
            Permissions =
            [
                new AbstractionsPermDef("pur.order.view", "查看采购订单", PermissionKind.Menu),
                new AbstractionsPermDef("pur.order.edit", "编辑采购订单", PermissionKind.Action),
                new AbstractionsPermDef("pur.order.approve", "审批采购订单", PermissionKind.Action),
            ],
        };
        var moduleV1 = new StubModule(manifest1);
        var service = CreateService([moduleV1]);
        await service.SyncModuleAsync(moduleV1, CancellationToken.None);

        // Arrange 续: Manifest 移除 pur.order.approve
        var manifest2 = manifest1 with
        {
            Permissions =
            [
                new AbstractionsPermDef("pur.order.view", "查看采购订单", PermissionKind.Menu),
                new AbstractionsPermDef("pur.order.edit", "编辑采购订单", PermissionKind.Action),
            ],
        };
        var moduleV2 = new StubModule(manifest2);

        // Act
        await service.SyncModuleAsync(moduleV2, CancellationToken.None);

        // Assert: approve 被软删，view/edit 仍激活
        using var db = _factory.CreateDbContext();
        var approve = await db.PermissionDefinitions
            .SingleAsync(p => p.PermissionCode == "pur.order.approve", CancellationToken.None);
        var view = await db.PermissionDefinitions
            .SingleAsync(p => p.PermissionCode == "pur.order.view", CancellationToken.None);
        var edit = await db.PermissionDefinitions
            .SingleAsync(p => p.PermissionCode == "pur.order.edit", CancellationToken.None);

        Assert.True(approve.IsDeprecated);
        Assert.False(view.IsDeprecated);
        Assert.False(edit.IsDeprecated);
    }

    // ==================== 恢复场景 ====================

    [Fact]
    public async Task SyncModuleAsyncDeprecatedPermissionReappearsRestoresIsDeprecatedFalse()
    {
        // Arrange: 先同步含 approve → 再软删 → 再恢复
        var manifest1 = new ModuleManifest
        {
            ModuleCode = "pur",
            DisplayName = "采购管理",
            TablePrefix = "pur_",
            Permissions =
            [
                new AbstractionsPermDef("pur.order.approve", "审批采购订单", PermissionKind.Action),
            ],
        };
        var moduleV1 = new StubModule(manifest1);
        var service = CreateService([moduleV1]);
        await service.SyncModuleAsync(moduleV1, CancellationToken.None);

        // 软删阶段
        var manifest2 = manifest1 with { Permissions = [] };
        var moduleV2 = new StubModule(manifest2);
        await service.SyncModuleAsync(moduleV2, CancellationToken.None);

        // 验证已软删
        using (var db = _factory.CreateDbContext())
        {
            var approve = await db.PermissionDefinitions
                .SingleAsync(p => p.PermissionCode == "pur.order.approve", CancellationToken.None);
            Assert.True(approve.IsDeprecated);
        }

        // Act: 恢复阶段
        var manifest3 = manifest1;
        var moduleV3 = new StubModule(manifest3);
        await service.SyncModuleAsync(moduleV3, CancellationToken.None);

        // Assert: IsDeprecated 恢复为 false
        using (var db3 = _factory.CreateDbContext())
        {
            var approve = await db3.PermissionDefinitions
                .SingleAsync(p => p.PermissionCode == "pur.order.approve", CancellationToken.None);
            Assert.False(approve.IsDeprecated);
        }
    }

    // ==================== Guard 拦截器 BypassGuard 场景 ====================

    [Fact]
    public async Task SyncAllAsyncSetsBypassGuardAllowingManifestWritesThroughInterceptor()
    {
        // Arrange
        var moduleA = new StubModule(new ModuleManifest
        {
            ModuleCode = "pur",
            DisplayName = "采购管理",
            TablePrefix = "pur_",
            Permissions =
            [
                new AbstractionsPermDef("pur.order.view", "查看采购订单", PermissionKind.Menu),
            ],
        });
        var moduleB = new StubModule(new ModuleManifest
        {
            ModuleCode = "sal",
            DisplayName = "销售管理",
            TablePrefix = "sal_",
            Permissions =
            [
                new AbstractionsPermDef("sal.order.view", "查看销售订单", PermissionKind.Menu),
                new AbstractionsPermDef("sal.order.create", "新建销售订单", PermissionKind.Action),
            ],
        });
        var service = CreateService([moduleA, moduleB]);

        // Guard 初始状态应为 false
        Assert.False(ManifestDefinitionReadOnlyGuardInterceptor.BypassGuard.Value);

        // Act: SyncAllAsync 内部会设置 BypassGuard=true
        await service.SyncAllAsync(CancellationToken.None);

        // Assert: BypassGuard 执行完后应恢复为 false
        Assert.False(ManifestDefinitionReadOnlyGuardInterceptor.BypassGuard.Value,
            "SyncAllAsync 执行完后 BypassGuard 应恢复为 false");

        using var db = _factory.CreateDbContext();
        var purPerms = await db.PermissionDefinitions
            .Where(p => p.ModuleCode == "pur").ToListAsync(CancellationToken.None);
        var salPerms = await db.PermissionDefinitions
            .Where(p => p.ModuleCode == "sal").ToListAsync(CancellationToken.None);

        Assert.Single(purPerms);
        Assert.Equal(2, salPerms.Count);
        Assert.All(purPerms.Concat(salPerms), p => Assert.False(p.IsDeprecated));
    }

    /// <summary>
    /// 补充：验证 BypassGuard 未设置时，直接写入清单表会被拦截器阻止（对照测试）。
    /// </summary>
    [Fact]
    public async Task GuardInterceptorWithoutBypassGuardThrowsOnDirectManifestWrite()
    {
        // Arrange: 确认 BypassGuard 处于默认 false
        Assert.False(ManifestDefinitionReadOnlyGuardInterceptor.BypassGuard.Value);

        // Act & Assert: 直接向 PermissionDefinitions Add 并 SaveChanges → 被拦截器阻止
        await using var db = _factory.CreateDbContext();
        var entity = new PortalPermDef
        {
            PermissionCode = "hack.manual.write",
            ModuleCode = "hack",
            Kind = PermissionKind.Action,
            DisplayName = "手工写入测试",
        };
        entity.AssignId(1L);
        await db.PermissionDefinitions.AddAsync(entity, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.SaveChangesAsync(CancellationToken.None));
        Assert.Contains("IModuleManifestSyncService", ex.Message);
    }

    // ==================== 辅助 ====================

    /// <summary>创建 PortalManifestSyncService 实例，依赖全部桩实现。</summary>
    private PortalManifestSyncService CreateService(IEnumerable<IModule> modules)
    {
        return new PortalManifestSyncService(
            _factory,
            new StubSnowflakeIdGenerator(),
            modules.ToList(),
            NullLogger<PortalManifestSyncService>.Instance);
    }
}
