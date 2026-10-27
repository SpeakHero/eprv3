/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 12:00:00 +08:00 中国标准时间 星期日
 * 描述：组织节点历史版本快照实体（bas_org_node_histories），每次写操作落一条扁平原始字段 JSON
 */

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain;

/// <summary>
/// 组织节点历史版本快照（表 bas_org_node_histories）。
/// <para>SnapshotJson 为组织节点在该版本的扁平原始字段序列化结果（不含展示态拼接，C-7-2）。
/// 同一组织同一版本唯一；按 EffectiveFrom 可回看任意日期生效的版本。</para>
/// </summary>
[Index(nameof(TenantId), nameof(OrgId), nameof(Version), IsUnique = true)]
public sealed class OrgNodeHistory : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>组织节点 Id（bas_organizations.id）。</summary>
    public long OrgId { get; internal set; }

    /// <summary>数据版本号。</summary>
    public int Version { get; internal set; }

    /// <summary>版本快照 JSON（扁平原始字段）。</summary>
    [MaxLength(4000)]
    public string SnapshotJson { get; internal set; } = string.Empty;

    /// <summary>该版本生效时间（UTC）。</summary>
    public DateTimeOffset EffectiveFrom { get; internal set; }

    /// <summary>变更人。</summary>
    [MaxLength(100)]
    public string ChangedBy { get; internal set; } = string.Empty;

    public OrgNodeHistory() { }

    public OrgNodeHistory(
        long tenantId,
        long orgId,
        int version,
        string snapshotJson,
        DateTimeOffset effectiveFrom,
        string changedBy)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orgId);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        TenantId = tenantId;
        OrgId = orgId;
        Version = version;
        SnapshotJson = snapshotJson;
        EffectiveFrom = effectiveFrom;
        ChangedBy = changedBy;
    }
}
