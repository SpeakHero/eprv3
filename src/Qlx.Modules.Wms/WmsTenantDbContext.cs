/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：Wms（物流管理）模块租户级 DbContext，表前缀 wms_，继承 TenantDbContext 自动路由租户库 + 全局租户过滤
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.Modules.Wms.Domain.Adjust;
using Qlx.Modules.Wms.Domain.Inbound;
using Qlx.Modules.Wms.Domain.Numbering;
using Qlx.Modules.Wms.Domain.Outbound;
using Qlx.Modules.Wms.Domain.Outbox;
using Qlx.Modules.Wms.Domain.StockTake;
using Qlx.Modules.Wms.Domain.Transfer;

namespace Qlx.Modules.Wms;

/// <summary>
/// Wms 模块租户 DbContext。构造由 ServiceCollection 工厂注册，动态注入当前请求的 tenantId。
/// 表名与列名 snake_case 由 <see cref="TenantDbContext"/> 基类统一转换，主键统一走雪花约定。
/// </summary>
public sealed partial class WmsTenantDbContext(
    ITenantDatabaseResolver resolver,
    long tenantId,
    DbContextOptions options)
    : TenantDbContext(resolver, tenantId, options)
{
    /// <inheritdoc />
    protected override string? GetModuleTablePrefix() => QlxModuleCodes.TablePrefix(QlxModuleCodes.Wms);

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        // 审批实例 Id 租户内唯一：仅对已回写实例 Id 的请求生效（发起审批前为 null）
        _ = modelBuilder.Entity<WmsApprovalRequest>()
            .HasIndex(r => new { r.TenantId, r.ApprovalInstanceId })
            .IsUnique()
            .HasFilter("approval_instance_id IS NOT NULL");

        // 下次重试时间以 UTC ticks（bigint）存储：SQLite 不支持 DateTimeOffset 比较，
        // 分发任务按「≤ 当前时间」筛选必须可翻译（SQL Server 侧为 bigint 列，语义不变）
        _ = modelBuilder.Entity<WmsStockCommandOutbox>()
            .Property(r => r.NextRetryAt)
            .HasConversion(new ValueConverter<DateTimeOffset, long>(
                v => v.UtcTicks,
                v => new DateTimeOffset(v, TimeSpan.Zero)));

        // 作业单 → 明细：从属关系，删单级联删明细（跨模块不建外键，仅模块内）
        _ = modelBuilder.Entity<StockInTask>()
            .HasMany(t => t.Lines)
            .WithOne()
            .HasForeignKey(l => l.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        _ = modelBuilder.Entity<StockOutTask>()
            .HasMany(t => t.Lines)
            .WithOne()
            .HasForeignKey(l => l.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        _ = modelBuilder.Entity<StockTransfer>()
            .HasMany(t => t.Lines)
            .WithOne()
            .HasForeignKey(l => l.TransferId)
            .OnDelete(DeleteBehavior.Cascade);

        _ = modelBuilder.Entity<StockTake>()
            .HasMany(t => t.Lines)
            .WithOne()
            .HasForeignKey(l => l.TakeId)
            .OnDelete(DeleteBehavior.Cascade);

        _ = modelBuilder.Entity<InventoryAdjustDoc>()
            .HasMany(d => d.Lines)
            .WithOne()
            .HasForeignKey(l => l.DocId)
            .OnDelete(DeleteBehavior.Cascade);

        SnowflakeKeyConvention.Apply(modelBuilder, this);
    }
}
