/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：租户升级表级复制断点实体，支撑失败续跑与共享库残留清理边界
 */

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Portal.Domain.Tenancy;

/// <summary>
/// 租户升级表级复制断点（表前缀 por_）。每张已复制表记录最大主键与累计行数：
/// 复制失败重试时从 LastKey 续跑；升级完成过保留期后，共享库清理服务按 LastKey 界定删除边界。
/// 继承 <see cref="Qlx.Abstractions.Domain.Entity"/>：仅雪花 Id，无审计需求。
/// </summary>
[Table("por_tenant_upgrade_checkpoints")]
[Index(nameof(JobId), nameof(ModuleCode), nameof(TableName), IsUnique = true)]
public sealed class TenantUpgradeCheckpoint : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属升级任务 ID（外键 → por_tenant_upgrade_jobs）。</summary>
    public long JobId { get; private set; }

    /// <summary>模块编码。</summary>
    [MaxLength(64)]
    public string ModuleCode { get; private set; } = string.Empty;

    /// <summary>表名。</summary>
    [MaxLength(128)]
    public string TableName { get; private set; } = string.Empty;

    /// <summary>已复制到的最大主键。</summary>
    public long LastKey { get; private set; }

    /// <summary>该表累计复制行数。</summary>
    public long RowsCopied { get; private set; }

    /// <summary>
    /// EF Core 无参构造。
    /// </summary>
    public TenantUpgradeCheckpoint() { }

    /// <summary>
    /// 创建表级断点。
    /// </summary>
    /// <param name="jobId">升级任务 ID。</param>
    /// <param name="moduleCode">模块编码。</param>
    /// <param name="tableName">表名。</param>
    /// <param name="lastKey">已复制到的最大主键。</param>
    /// <param name="rowsCopied">该表累计复制行数。</param>
    public TenantUpgradeCheckpoint(long jobId, string moduleCode, string tableName, long lastKey, long rowsCopied)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        JobId = jobId;
        ModuleCode = moduleCode;
        TableName = tableName;
        LastKey = lastKey;
        RowsCopied = rowsCopied;
    }

    /// <summary>
    /// 更新断点进度。
    /// </summary>
    /// <param name="lastKey">已复制到的最大主键。</param>
    /// <param name="rowsCopied">该表累计复制行数。</param>
    public void Update(long lastKey, long rowsCopied)
    {
        LastKey = lastKey;
        RowsCopied = rowsCopied;
    }
}
