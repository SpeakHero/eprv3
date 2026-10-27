/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 19:35:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 19:35:00 +08:00 中国标准时间 星期三
 * 描述：模块目录 / 许可记录 / 门户工作台概览服务实现
 */

using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Portal.Dtos;
using Qlx.Modules.Portal.Repositories;

namespace Qlx.Modules.Portal.Services;

/// <summary>模块目录与许可记录服务。</summary>
public sealed class PortalModuleAdminService(
    IPortalModuleAdminRepository modules,
    IPortalTenantAdminRepository tenants,
    IIdObfuscator obfuscator) : IPortalModuleAdminService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PortalModuleItem>> ListModulesAsync(CancellationToken cancellationToken)
    {
        var catalog = await modules.ListCatalogAsync(cancellationToken).ConfigureAwait(false);

        return [.. catalog.Select(m => new PortalModuleItem
        {
            Code = m.ModuleCode,
            Name = m.DisplayName,
            Version = m.CurrentVersion,
            Description = m.Description ?? string.Empty,
            Features = m.Features,
            LicensedTenantCount = m.LicensedTenantCount,
        })];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LicenseRecordItem>> ListLicenseRecordsAsync(
        CancellationToken cancellationToken)
    {
        var records = await tenants.QueryLicenseRecordsAsync(null, 100, cancellationToken).ConfigureAwait(false);
        return [.. records.Select(r => PortalAdminMapper.ToLicenseRecordItem(r, obfuscator))];
    }
}

/// <summary>门户管理工作台概览服务。</summary>
public sealed class PortalOverviewService(IPortalModuleAdminRepository modules) : IPortalOverviewService
{
    /// <inheritdoc />
    public async Task<PortalWorkbenchOverviewResponse> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var counters = await modules.GetWorkbenchCountersAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        var stats = new List<PortalAdminStatItem>
        {
            new()
            {
                Key = "tenant-total",
                Label = "租户总数",
                Value = counters.TenantTotal,
                Unit = "家",
                Hint = $"预配中 {counters.TenantPending} 家",
                Icon = "building",
                Tone = "brand",
            },
            new()
            {
                Key = "tenant-enabled",
                Label = "启用租户",
                Value = counters.TenantEnabled,
                Unit = "家",
                Hint = "可正常进入租户上下文",
                Icon = "check",
                Tone = "success",
            },
            new()
            {
                Key = "portal-user",
                Label = "门户用户",
                Value = counters.UserTotal,
                Unit = "人",
                Hint = $"已停用 {counters.UserDisabled} 人",
                Icon = "users",
                Tone = "gold",
            },
            new()
            {
                Key = "module-license",
                Label = "模块授权",
                Value = counters.LicensedTenantModuleCount,
                Unit = "项",
                Hint = "按租户模块许可累计",
                Icon = "grid",
                Tone = "warning",
            },
        };

        var todos = new List<PortalAdminTodoItem>();
        if (counters.TenantProvisioningFailed > 0)
        {
            todos.Add(new PortalAdminTodoItem
            {
                Id = "tenant-provisioning-failed",
                Type = "租户预配",
                Title = $"{counters.TenantProvisioningFailed} 个租户预配失败，待人工处理",
                At = now,
                Level = "danger",
                Href = "/portal/tenants?status=ProvisioningFailed",
            });
        }

        if (counters.TenantPending > 0)
        {
            todos.Add(new PortalAdminTodoItem
            {
                Id = "tenant-provisioning",
                Type = "租户预配",
                Title = $"{counters.TenantPending} 个租户正在初始化独立数据库",
                At = now,
                Level = "info",
                Href = "/portal/tenants?status=Pending",
            });
        }

        if (counters.UserDisabled > 0)
        {
            todos.Add(new PortalAdminTodoItem
            {
                Id = "user-disabled",
                Type = "账号安全",
                Title = $"{counters.UserDisabled} 个门户用户处于停用状态",
                At = now,
                Level = "warning",
                Href = "/portal/users?status=Disabled",
            });
        }

        if (todos.Count == 0)
        {
            todos.Add(new PortalAdminTodoItem
            {
                Id = "all-clear",
                Type = "运行状态",
                Title = "门户运行正常，暂无待处理事项",
                At = now,
                Level = "info",
                Href = "/portal/tenants",
            });
        }

        return new PortalWorkbenchOverviewResponse(stats, todos);
    }
}
