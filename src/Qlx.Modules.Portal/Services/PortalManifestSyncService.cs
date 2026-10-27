/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 22:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 22:30:00 +08:00 中国标准时间 星期四
 * 描述：ModuleManifestSyncService 默认实现：把各模块 Manifest 的权限点/菜单/导航分组物化到 Portal 库
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Qlx.Abstractions.Modules;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.Modules;
using PortalPermDef = Qlx.Modules.Portal.Domain.Tenancy.PermissionDefinition;
using PortalMenuDef = Qlx.Modules.Portal.Domain.Tenancy.ModuleMenu;
using PortalNavGroup = Qlx.Modules.Portal.Domain.Tenancy.NavigationGroup;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// Portal 侧 ModuleManifestSyncService 默认实现。
/// 宿主启动期在 MigrateAsync 之后调用 <see cref="SyncAllAsync"/>，
/// 将所有已装配 <see cref="IModule"/> 的 Manifest 物化到 Portal 管理库。
/// <para>
/// 写入前设置 <see cref="ManifestDefinitionReadOnlyGuardInterceptor.BypassGuard"/> 放行；
/// 每个模块同步使用独立事务，单模块失败不阻塞其他模块。
/// <b>PortalRole / PortalRolePermission / PortalUserRole 不在同步范围内</b>——门户侧角色授权由管理员手动配置，
/// 不跟随 Manifest 自动变更。
/// </para>
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
/// <param name="dbFactory">PortalDbContext 工厂，创建时已注入 ManifestDefinitionReadOnlyGuardInterceptor。</param>
/// <param name="snowflake">雪花 Id 生成器，为新增的清单实体分配全局唯一主键。</param>
/// <param name="modules">宿主扫描到的全部 IModule 实例集合（单例）。</param>
/// <param name="cache">统一缓存服务（同步完成后失效菜单投影缓存，G-4）。</param>
/// <param name="logger">日志器。</param>
public sealed partial class PortalManifestSyncService(
    IDbContextFactory<PortalDbContext> dbFactory,
    ISnowflakeIdGenerator snowflake,
    IReadOnlyList<IModule> modules,
    ICacheService cache,
    ILogger<PortalManifestSyncService> logger) : IModuleManifestSyncService
{
    // =====================================================================
    // 公共契约实现
    // =====================================================================

    /// <inheritdoc />
    public async Task SyncAllAsync(CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(modules);

        // 宿主以 IReadOnlyList<IModule> 单例注册（AddQlxModules），不可用 IEnumerable<IModule>
        // 注入：DI 只从逐项注册的 IModule 解析 IEnumerable，会导致空集合、权限点从未物化。
        var moduleList = modules;
        LogSyncAllStart(moduleList.Count);

        foreach (var module in moduleList)
        {
            try
            {
                await SyncModuleInternalAsync(module, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 单模块同步失败不阻塞其他模块：记录后继续
                LogSyncModuleFailed(module.Manifest.ModuleCode, ex);
            }
        }

        // ======= 顶级导航分组：跨模块平台级配置，统一 Upsert + 软删 =======
        // TopNavigationGroups 仅 Portal 模块声明，全局唯一键为 GroupCode（跨模块共享 por_navigation_groups）。
        // 单独处理而不嵌入 SyncModuleInternalAsync，避免其他无 TopNavigationGroups 的模块
        // 把 Portal 模块已写入的导航分组误标记为 IsDeprecated。
        await SyncTopNavigationGroupsAsync(moduleList, ct).ConfigureAwait(false);

        // 清单变更即菜单投影失效（C-5 失效优先：DB 已提交后再删缓存，后续读经 GetOrCreateAsync 重建）
        await cache.RemoveByPrefixAsync(PortalConstants.CacheKeys.MenuPrefix, ct).ConfigureAwait(false);

        LogSyncAllDone();
    }

    /// <inheritdoc />
    public Task SyncModuleAsync(IModule moduleInstance, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(moduleInstance);
        return SyncModuleInternalAsync(moduleInstance, ct);
    }

    // =====================================================================
    // 内部实现
    // =====================================================================

    /// <summary>
    /// 单个模块的权限点 + 菜单同步，包裹 BypassGuard + 独立事务。
    /// </summary>
    private async Task SyncModuleInternalAsync(IModule module, CancellationToken ct)
    {
        var manifest = module.Manifest;
        LogSyncModuleStart(manifest.ModuleCode);

        // 1. 放行只读守卫拦截器（try-finally 保证清理，即使异常也会恢复默认）
        ManifestDefinitionReadOnlyGuardInterceptor.BypassGuard.Value = true;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            // 2. 权限点 Upsert + 软删
            await SyncPermissionsAsync(db, manifest, ct).ConfigureAwait(false);

            // 3. 菜单 Upsert + 软删
            await SyncMenusAsync(db, manifest, ct).ConfigureAwait(false);

            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            ManifestDefinitionReadOnlyGuardInterceptor.BypassGuard.Value = false;
        }

        LogSyncModuleDone(manifest.ModuleCode);
    }

    // =====================================================================
    // 权限点同步
    // =====================================================================

    /// <summary>
    /// Upsert 该模块 Manifest 的 PermissionDefinition → por_permission_definitions。
    /// 存在则 Skip（Manifest 不可变，不 Update）；新增则 Insert；Manifest 已移除标记 IsDeprecated=true。
    /// </summary>
    private async Task SyncPermissionsAsync(PortalDbContext db, ModuleManifest manifest, CancellationToken ct)
    {
        var moduleCode = manifest.ModuleCode;
        var manifestPerms = manifest.Permissions;

        // 1. 读取该模块当前所有已登记的权限点（含 IsDeprecated）
        var existing = await db.PermissionDefinitions
            .AsNoTracking()
            .Where(p => p.ModuleCode == moduleCode)
            .ToListAsync(ct).ConfigureAwait(false);

        var existingByCode = new Dictionary<string, PortalPermDef>(
            existing.Count, StringComparer.Ordinal);
        foreach (var p in existing)
        {
            existingByCode.TryAdd(p.PermissionCode, p);
        }

        // 2. 新增或激活 Manifest 中声明的权限点
        foreach (var mp in manifestPerms)
        {
            if (existingByCode.TryGetValue(mp.Code, out var existingEntity))
            {
                // 已存在：若之前被软删则恢复（重新激活）
                var entry = db.PermissionDefinitions.Update(existingEntity);
                if (existingEntity.IsDeprecated)
                {
                    entry.CurrentValues[nameof(PortalPermDef.IsDeprecated)] = false;
                    entry.State = EntityState.Modified;
                }
                else
                {
                    entry.State = EntityState.Unchanged; // Manifest 不可变，跳过 Update
                }
            }
            else
            {
                var entity = new PortalPermDef
                {
                    PermissionCode = mp.Code,
                    ModuleCode = moduleCode,
                    Kind = mp.Kind,
                    DisplayName = mp.Name,
                };
                entity.AssignId(snowflake.NextId());
                await db.PermissionDefinitions.AddAsync(entity, ct).ConfigureAwait(false);
            }
        }

        // 3. 软删 Manifest 已移除的权限点
        var manifestCodeSet = manifestPerms.Select(static p => p.Code)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var existingEntity in existing)
        {
            if (!manifestCodeSet.Contains(existingEntity.PermissionCode) && !existingEntity.IsDeprecated)
            {
                var entry = db.PermissionDefinitions.Update(existingEntity);
                entry.CurrentValues[nameof(PortalPermDef.IsDeprecated)] = true;
                entry.State = EntityState.Modified;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // =====================================================================
    // 菜单同步
    // =====================================================================

    /// <summary>
    /// Upsert 该模块 Manifest 的 MenuDefinition → por_module_menus。
    /// 存在则 Skip；新增则 Insert；Manifest 已移除标记 IsDeprecated=true。
    /// </summary>
    private async Task SyncMenusAsync(PortalDbContext db, ModuleManifest manifest, CancellationToken ct)
    {
        var moduleCode = manifest.ModuleCode;
        var manifestMenus = manifest.Menus;

        var existing = await db.ModuleMenus
            .AsNoTracking()
            .Where(m => m.ModuleCode == moduleCode)
            .ToListAsync(ct).ConfigureAwait(false);

        var existingByCode = new Dictionary<string, PortalMenuDef>(
            existing.Count, StringComparer.Ordinal);
        foreach (var m in existing)
        {
            existingByCode.TryAdd(m.MenuCode, m);
        }

        foreach (var mm in manifestMenus)
        {
            if (existingByCode.TryGetValue(mm.Code, out var existingEntity))
            {
                var entry = db.ModuleMenus.Update(existingEntity);
                if (existingEntity.IsDeprecated)
                {
                    entry.CurrentValues[nameof(PortalMenuDef.IsDeprecated)] = false;
                    entry.State = EntityState.Modified;
                }
                else
                {
                    entry.State = EntityState.Unchanged;
                }
            }
            else
            {
                var entity = new PortalMenuDef
                {
                    ModuleCode = moduleCode,
                    MenuCode = mm.Code,
                    ParentCode = mm.ParentCode,
                    DisplayName = mm.Name,
                    Route = mm.Route,
                    Icon = mm.Icon,
                    RequiredPermissionCode = mm.RequiredPermissionCode,
                    SortOrder = mm.SortOrder,
                };
                entity.AssignId(snowflake.NextId());
                await db.ModuleMenus.AddAsync(entity, ct).ConfigureAwait(false);
            }
        }

        var manifestCodeSet = manifestMenus.Select(static m => m.Code)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var existingEntity in existing)
        {
            if (!manifestCodeSet.Contains(existingEntity.MenuCode) && !existingEntity.IsDeprecated)
            {
                var entry = db.ModuleMenus.Update(existingEntity);
                entry.CurrentValues[nameof(PortalMenuDef.IsDeprecated)] = true;
                entry.State = EntityState.Modified;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // =====================================================================
    // 顶级导航分组同步（跨模块平台级，只在 SyncAllAsync 结束时统一处理）
    // =====================================================================

    /// <summary>
    /// 从所有模块收集 TopNavigationGroups → Upsert + 软删。
    /// 仅 Portal 模块会声明 TopNavigationGroups，其他模块保持空数组。
    /// </summary>
    private async Task SyncTopNavigationGroupsAsync(IEnumerable<IModule> allModules, CancellationToken ct)
    {
        var combined = new List<NavigationGroupDefinition>();
        foreach (var module in allModules)
        {
            combined.AddRange(module.Manifest.TopNavigationGroups);
        }

        // 跳过无任何 TopNavigationGroups 的场景（早期 Portal 模块可能还没声明）
        if (combined.Count == 0) return;

        ManifestDefinitionReadOnlyGuardInterceptor.BypassGuard.Value = true;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            var existing = await db.NavigationGroups
                .AsNoTracking()
                .ToListAsync(ct).ConfigureAwait(false);

            var existingByCode = new Dictionary<string, PortalNavGroup>(
                existing.Count, StringComparer.Ordinal);
            foreach (var g in existing)
            {
                existingByCode.TryAdd(g.GroupCode, g);
            }

            foreach (var mg in combined)
            {
                if (existingByCode.TryGetValue(mg.Code, out var existingEntity))
                {
                    var entry = db.NavigationGroups.Update(existingEntity);
                    if (existingEntity.IsDeprecated)
                    {
                        entry.CurrentValues[nameof(PortalNavGroup.IsDeprecated)] = false;
                        entry.State = EntityState.Modified;
                    }
                    else
                    {
                        entry.State = EntityState.Unchanged;
                    }
                }
                else
                {
                    var entity = new PortalNavGroup
                    {
                        GroupCode = mg.Code,
                        GroupName = mg.Name,
                        ModuleCodes = mg.ModuleCodes,
                        SortOrder = mg.SortOrder,
                    };
                    entity.AssignId(snowflake.NextId());
                    await db.NavigationGroups.AddAsync(entity, ct).ConfigureAwait(false);
                }
            }

            var manifestCodeSet = combined.Select(static g => g.Code)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var existingEntity in existing)
            {
                if (!manifestCodeSet.Contains(existingEntity.GroupCode) && !existingEntity.IsDeprecated)
                {
                    var entry = db.NavigationGroups.Update(existingEntity);
                    entry.CurrentValues[nameof(PortalNavGroup.IsDeprecated)] = true;
                    entry.State = EntityState.Modified;
                }
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            ManifestDefinitionReadOnlyGuardInterceptor.BypassGuard.Value = false;
        }
    }

    // =====================================================================
    // 日志
    // =====================================================================

    [LoggerMessage(Level = LogLevel.Information, Message = "开始同步所有模块 Manifest，共 {ModuleCount} 个模块")]
    private partial void LogSyncAllStart(int moduleCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "所有模块 Manifest 同步完成")]
    private partial void LogSyncAllDone();

    [LoggerMessage(Level = LogLevel.Information, Message = "开始同步模块 {ModuleCode} 的 Manifest")]
    private partial void LogSyncModuleStart(string moduleCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "模块 {ModuleCode} Manifest 同步完成")]
    private partial void LogSyncModuleDone(string moduleCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "模块 {ModuleCode} Manifest 同步失败（不阻塞其他模块）")]
    private partial void LogSyncModuleFailed(string moduleCode, Exception ex);
}
