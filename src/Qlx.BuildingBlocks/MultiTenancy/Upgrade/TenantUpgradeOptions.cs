/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：租户升级（共享库 → 独立库）配置项，控制调度周期、复制批量、抽样校验、共享数据清理
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.BuildingBlocks.MultiTenancy.Upgrade;

/// <summary>
/// 租户升级配置项。对应配置节 <c>Qlx:Tenancy:Upgrade</c>。
/// </summary>
public sealed record TenantUpgradeOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Qlx:Tenancy:Upgrade";

    /// <summary>是否启用升级调度后台服务，默认 true。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>待处理任务扫描周期（秒），默认 15。</summary>
    [Range(5, 3600)]
    public int ScanIntervalSeconds { get; init; } = 15;

    /// <summary>数据复制批量（每批行数），默认 1000。</summary>
    [Range(100, 100000)]
    public int BatchSize { get; init; } = 1000;

    /// <summary>校验阶段每表抽样比对行数，默认 100；0 表示只做行数比对。</summary>
    [Range(0, 10000)]
    public int VerifySampleSize { get; init; } = 100;

    /// <summary>升级完成后共享库数据保留天数（保留期过后由清理服务删除），默认 30。</summary>
    [Range(1, 365)]
    public int RetentionDays { get; init; } = 30;

    /// <summary>共享库数据清理批量（每次 DELETE TOP 行数），默认 5000。</summary>
    [Range(100, 100000)]
    public int CleanBatchSize { get; init; } = 5000;

    /// <summary>共享库数据清理扫描周期（小时），默认 24。</summary>
    [Range(1, 168)]
    public int CleanIntervalHours { get; init; } = 24;
}
