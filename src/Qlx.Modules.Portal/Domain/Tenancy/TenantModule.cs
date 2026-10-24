/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 23:00:00 +08:00 中国标准时间 星期二
 * 描述：租户模块许可实体，控制各模块的启停与版本
 */

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Portal.Domain.Tenancy;

/// <summary>
/// 租户模块许可状态。
/// </summary>
public enum TenantModuleStatus
{
    /// <summary>未授权（门户侧未授予该模块）。</summary>
    NotLicensed = 0,

    /// <summary>已启用（运行时可正常访问）。</summary>
    Enabled = 1,

    /// <summary>已停用（运行时返回 404、任务不调度、事件订阅不激活）。</summary>
    Disabled = 2,
}

/// <summary>
/// 租户模块许可实体（表前缀 por_）。门户侧按需授权，
/// 租户侧实时按本表过滤路由/菜单/任务/事件订阅。
/// 继承 <see cref="Qlx.Abstractions.Domain.AuditableEntity"/>：雪花 Id + 审计字段由拦截器自动填充。
/// 原业务唯一键（TenantId + ModuleCode）改为唯一索引。
/// </summary>
[Table("por_tenant_modules")]
[Index(nameof(TenantId), nameof(ModuleCode), IsUnique = true)]
[Index(nameof(ModuleCode))]
[Index(nameof(Status))]
public sealed class TenantModule : Qlx.Abstractions.Domain.AuditableEntity
{
    /// <summary>所属租户 ID（外键 → por_tenants）。</summary>
    public long TenantId { get; internal set; }

    /// <summary>模块编码（必须与 IModule.Manifest.ModuleCode 一致）。</summary>
    [MaxLength(64)]
    public string ModuleCode { get; internal set; } = string.Empty;

    /// <summary>模块许可状态。</summary>
    public TenantModuleStatus Status { get; private set; } = TenantModuleStatus.Enabled;

    /// <summary>授权版本（模块 SemVer）。</summary>
    [MaxLength(32)]
    public string? LicensedVersion { get; private set; }

    /// <summary>授权到期时间（与租户订阅到期可能不同，模块可单独续费）。</summary>
    public DateTimeOffset? LicensedUntil { get; private set; }

    /// <summary>自定义许可 JSON（预留扩展字段，如用户数上限、存储空间配额等）。</summary>
    [MaxLength(2048)]
    public string? CustomLicenseData { get; private set; }

    /// <summary>授权时间（UTC）。</summary>
    public DateTimeOffset LicensedAt { get; internal set; } = DateTimeOffset.UtcNow;

    /// <summary>关联租户（多对一导航）。</summary>
    public Tenant? Tenant { get; internal set; }

    /// <summary>
    /// EF Core 无参构造。
    /// </summary>
    public TenantModule() { }

    /// <summary>
    /// 创建租户模块许可。
    /// </summary>
    public TenantModule(Tenant tenant, string moduleCode, string? licensedVersion = null)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleCode);
        Tenant = tenant;
        ModuleCode = moduleCode;
        LicensedVersion = licensedVersion;
    }

    /// <summary>
    /// 启用模块。
    /// </summary>
    public void Enable() => Status = TenantModuleStatus.Enabled;

    /// <summary>
    /// 停用模块（运行时立即生效：路由 404、任务暂停、事件不激活）。
    /// </summary>
    public void Disable() => Status = TenantModuleStatus.Disabled;
}
