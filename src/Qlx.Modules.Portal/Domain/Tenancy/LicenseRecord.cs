/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 16:00:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 16:00:00 +08:00 中国标准时间 星期三
 * 描述：模块许可变更记录实体（por_license_records），门户管理端授权记录展示
 */

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Portal.Domain.Tenancy;

/// <summary>
/// 许可变更动作。
/// </summary>
public enum LicenseAction
{
    /// <summary>开通模块。</summary>
    Granted = 1,

    /// <summary>停用模块。</summary>
    Revoked = 2,

    /// <summary>授权内容调整。</summary>
    Adjusted = 3,
}

/// <summary>
/// 模块许可变更记录（表前缀 por_）。每次整单保存租户模块授权时按差异逐模块落一条记录，
/// 供门户管理端「授权记录」追溯。租户名称与模块名称作冗余快照，避免历史记录随主数据改名漂移。
/// </summary>
[Table("por_license_records")]
[Index(nameof(TenantId))]
[Index(nameof(At))]
public sealed class LicenseRecord : Qlx.Abstractions.Domain.AuditableEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; private init; }

    /// <summary>租户名称快照。</summary>
    [MaxLength(256)]
    public string TenantName { get; private init; } = string.Empty;

    /// <summary>模块编码。</summary>
    [MaxLength(64)]
    public string ModuleCode { get; private init; } = string.Empty;

    /// <summary>模块名称快照。</summary>
    [MaxLength(128)]
    public string ModuleName { get; private init; } = string.Empty;

    /// <summary>变更动作。</summary>
    public LicenseAction Action { get; private init; }

    /// <summary>操作人显示名。</summary>
    [MaxLength(64)]
    public string OperatorName { get; private init; } = string.Empty;

    /// <summary>变更备注。</summary>
    [MaxLength(512)]
    public string Remark { get; private init; } = string.Empty;

    /// <summary>变更时间（UTC）。</summary>
    public DateTimeOffset At { get; private init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// EF Core 无参构造。
    /// </summary>
    public LicenseRecord() { }

    /// <summary>
    /// 创建许可变更记录。
    /// </summary>
    /// <param name="tenant">所属租户。</param>
    /// <param name="moduleCode">模块编码。</param>
    /// <param name="moduleName">模块名称快照。</param>
    /// <param name="action">变更动作。</param>
    /// <param name="operatorName">操作人显示名。</param>
    /// <param name="remark">备注。</param>
    public LicenseRecord(
        Tenant tenant,
        string moduleCode,
        string moduleName,
        LicenseAction action,
        string operatorName,
        string remark)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);

        TenantId = tenant.Id;
        TenantName = tenant.DisplayName;
        ModuleCode = moduleCode;
        ModuleName = moduleName;
        Action = action;
        OperatorName = operatorName;
        Remark = remark;
        At = DateTimeOffset.UtcNow;
    }
}
