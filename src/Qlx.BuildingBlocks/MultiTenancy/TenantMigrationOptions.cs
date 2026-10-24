/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 03:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 05:30:00 +08:00 中国标准时间 星期三
 * 描述：租户迁移执行配置项，控制后台扫描周期、并发度、灰度分批、失败重试
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 租户迁移执行配置项。对应配置节 <c>Qlx:MultiTenancy</c>。
/// </summary>
public sealed record TenantMigrationOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Qlx:MultiTenancy";

    /// <summary>扫描周期（秒），默认 60。</summary>
    [Range(5, 3600)]
    public int ScanIntervalSeconds { get; init; } = 60;

    /// <summary>并发执行迁移的租户数上限，默认 4。</summary>
    [Range(1, 32)]
    public int MigrationConcurrency { get; init; } = 4;

    /// <summary>单模块迁移超时（秒），默认 300。</summary>
    [Range(30, 3600)]
    public int ModuleTimeoutSeconds { get; init; } = 300;

    /// <summary>单次扫描最多处理的租户数（灰度分批），默认 5；0 表示不限制。</summary>
    [Range(0, 1000)]
    public int BatchSize { get; init; } = 5;

    /// <summary>单租户最大预配尝试次数（超过后标记 ProvisioningFailed 并停止重试），默认 3。</summary>
    [Range(1, 10)]
    public int MaxRetryCount { get; init; } = 3;

    /// <summary>
    /// 迁移应用锁（<c>sp_getapplock</c>）等待超时（秒），默认 60；用于多 Host 并发启动时只迁一次。
    /// </summary>
    [Range(1, 3600)]
    public int MigrationLockTimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// 首个模块迁移失败时是否允许清理（DROP）独立租户库后重试，**默认 false**。
    /// 共享库永不清理，本开关仅对独立租户库生效。
    /// </summary>
    public bool AllowDropOnFirstModuleFailure { get; init; }

    /// <summary>
    /// 单租户最大并发连接数（SemaphoreSlim 闸门），默认 50。
    /// 控制 50+ 租户场景下的总连接池上限，避免 ADO.NET 连接池数量 × 租户数 = 连接数爆炸。
    /// </summary>
    [Range(1, 500)]
    public int MaxConcurrentConnectionsPerTenant { get; init; } = 50;

    /// <summary>创建数据库时的默认 master 连接串，开发期为空时用 LocalDB 占位。</summary>
    public string? MasterConnectionString { get; init; }
}
