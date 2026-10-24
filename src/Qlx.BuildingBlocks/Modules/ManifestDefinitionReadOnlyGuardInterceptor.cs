/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 22:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 22:30:00 +08:00 中国标准时间 星期四
 * 描述：Manifest 定义只读守卫拦截器，阻止非同步服务对 Portal 库清单表的直接写入
 */

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Qlx.BuildingBlocks.Modules;

/// <summary>
/// Portal 库清单表只读守卫拦截器。
/// 所有 <see cref="IModuleManifestSyncService"/> 以外的 SaveChanges 调用如果试图对
/// ModuleDefinition / PermissionDefinition / ModuleMenu / NavigationGroup 执行 Added/Modified/Deleted，
/// 会立即抛出 <see cref="InvalidOperationException"/>，强制所有清单变更走同步服务的统一入口。
/// <para>拦截器通过 <see cref="BypassGuard"/>（AsyncLocal）放行同步服务自身的写入；
/// 同时用 DbContext / 实体类型名（FullName）做松耦合匹配，避免 BuildingBlocks → Portal 的反向引用。</para>
/// </summary>
public sealed class ManifestDefinitionReadOnlyGuardInterceptor : SaveChangesInterceptor
{
    /// <summary>
    /// 旁路开关。<see cref="IModuleManifestSyncService.SyncModuleAsync"/> 在写入前设为 <c>true</c>，
    /// 写入完成后恢复为默认值，让拦截器放行本次同步操作。
    /// </summary>
    public static readonly AsyncLocal<bool> BypassGuard = new();

    /// <summary>Portal DbContext 类型名（松耦合匹配，避免项目引用）。</summary>
    private const string PortalDbContextFullName = "Qlx.Modules.Portal.PortalDbContext";

    /// <summary>清单实体类型全名集合（松耦合匹配，避免项目引用）。</summary>
    private static readonly HashSet<string> GuardedEntityFullNames = new(StringComparer.Ordinal)
    {
        "Qlx.Modules.Portal.Domain.Tenancy.ModuleDefinition",
        "Qlx.Modules.Portal.Domain.Tenancy.PermissionDefinition",
        "Qlx.Modules.Portal.Domain.Tenancy.ModuleMenu",
        "Qlx.Modules.Portal.Domain.Tenancy.NavigationGroup",
    };

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Guard(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Guard(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private static void Guard(Microsoft.EntityFrameworkCore.DbContext? context)
    {
        if (context is null) return;
        if (BypassGuard.Value) return;
        if (context.GetType().FullName != PortalDbContextFullName) return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (Microsoft.EntityFrameworkCore.EntityState.Added
                                   or Microsoft.EntityFrameworkCore.EntityState.Modified
                                   or Microsoft.EntityFrameworkCore.EntityState.Deleted))
            {
                continue;
            }

            var entityTypeFullName = entry.Entity.GetType().FullName;
            if (entityTypeFullName is not null && GuardedEntityFullNames.Contains(entityTypeFullName))
            {
                throw new InvalidOperationException(
                    $"禁止通过非同步服务写入 Portal 清单表：{entityTypeFullName}。" +
                    $"所有清单变更必须走 IModuleManifestSyncService。若确需旁路，请设置 ManifestDefinitionReadOnlyGuardInterceptor.BypassGuard = true。");
            }
        }
    }
}
