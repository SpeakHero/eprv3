/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：租户隔离周期巡检配置项，控制巡检开关与周期
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.BuildingBlocks.MultiTenancy.Upgrade;

/// <summary>
/// 租户隔离周期巡检配置项。对应配置节 <c>Qlx:Tenancy:IsolationAudit</c>。
/// </summary>
public sealed record TenantIsolationAuditOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Qlx:Tenancy:IsolationAudit";

    /// <summary>是否启用周期巡检后台服务，默认 true。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>巡检周期（小时），默认 24。</summary>
    [Range(1, 168)]
    public int IntervalHours { get; init; } = 24;
}
