/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21
 * 描述：跨模块模块定义/权限点/租户授权状态只读查询实现。
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Queries;
using Qlx.Modules.Portal.Domain.Tenancy;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// <see cref="IModulePermissionQuery"/> 实现——从 Portal 库 ModuleDefinition/PermissionDefinition/TenantModule 投影，
/// 业务模块（如 Bas）消费以构建权限点下拉数据和租户已授权模块卡片。
/// </summary>
internal sealed class ModulePermissionQuery(
    IDbContextFactory<PortalDbContext> dbFactory) : IModulePermissionQuery
{
    public async Task<IReadOnlyList<ModuleDefinitionRow>> ListAllModulesWithPermissionsAsync(
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var permissions = await db.Set<PermissionDefinition>()
            .OrderBy(p => p.ModuleCode).ThenBy(p => p.SortOrder)
            .Select(p => new
            {
                p.ModuleCode,
                Row = new PermissionDefinitionRow(p.PermissionCode, p.DisplayName, p.Description),
            })
            .ToListAsync(cancellationToken);

        var permsByModule = permissions.GroupBy(x => x.ModuleCode, x => x.Row);

        var modules = await db.Set<ModuleDefinition>()
            .OrderBy(m => m.ModuleCode)
            .Select(m => new { m.ModuleCode, m.DisplayName, m.Description, m.CurrentVersion })
            .ToListAsync(cancellationToken);

        return [.. modules.Select(m => new ModuleDefinitionRow(
            m.ModuleCode,
            m.DisplayName,
            m.Description,
            null,
            m.CurrentVersion,
            permsByModule.FirstOrDefault(g => g.Key == m.ModuleCode)?.ToList() ?? [])
        )];
    }

    public async Task<IReadOnlyList<TenantModuleCardRow>> ListTenantModulesAsync(
        long tenantId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        // 注：Join 会产生 TransparentIdentifier<TenantModule, ModuleDefinition>，
        // EF Core 无法在 OrderBy 里直接引用 resultSelector 投影后的 TenantModuleCardRow.Code 属性。
        // 因此先投影到匿名类型（SQL 可翻译），拉到内存后再排序 + 构造最终 DTO。
        var raw = await (
            from tm in db.Set<TenantModule>().Where(tm => tm.TenantId == tenantId)
            join md in db.Set<ModuleDefinition>() on tm.ModuleCode equals md.ModuleCode
            select new
            {
                tm.ModuleCode,
                md.DisplayName,
                Description = md.Description ?? string.Empty,
                LicensedVersion = tm.LicensedVersion ?? md.CurrentVersion,
                Enabled = tm.Status == TenantModuleStatus.Enabled,
                tm.LicensedAt,
            })
            .ToListAsync(cancellationToken);

        return [.. raw
            .OrderBy(r => r.ModuleCode, StringComparer.Ordinal)
            .Select(r => new TenantModuleCardRow(
                r.ModuleCode,
                r.DisplayName,
                r.Description,
                null,
                r.LicensedVersion,
                r.Enabled,
                r.LicensedAt))];
    }
}
