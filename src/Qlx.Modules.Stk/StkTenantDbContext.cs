/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 14:00:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 20:00:00 +08:00 中国标准时间 星期一
 * 描述：Stk（商品/库存）租户 DbContext——表前缀 stk_，雪花 Id 值生成器，租户/软删全局过滤由基类注入；
 *       台账 Version 并发令牌 + 流水幂等唯一索引 + 流水只增守护（A-7）
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.Modules.Stk.Domain.Inventory;
using Qlx.Modules.Stk.Domain.Item;
using Qlx.Modules.Stk.Domain.Numbering;

namespace Qlx.Modules.Stk;

/// <summary>
/// Stk 模块租户 DbContext。表名 = <c>stk_</c> + snake_case(DbSet 属性名)；
/// 连接串由基类按租户动态路由解析，禁止重写 <c>OnConfiguring</c>。
/// </summary>
/// <param name="resolver">租户数据库解析器。</param>
/// <param name="tenantId">当前租户 Id。</param>
/// <param name="options">DbContext 选项。</param>
public sealed partial class StkTenantDbContext(
    ITenantDatabaseResolver resolver,
    long tenantId,
    DbContextOptions options) : TenantDbContext(resolver, tenantId, options)
{
    /// <inheritdoc />
    protected override string? GetModuleTablePrefix()
        => QlxModuleCodes.TablePrefix(QlxModuleCodes.Stk);

    /// <inheritdoc />
    /// <remarks>
    /// 数据注解无法表达过滤索引：商品条码可空且唯一性只对未删除行生效；
    /// 审批挂起请求的 <c>ApprovalInstanceId</c> 在发起审批前为 null，
    /// 唯一索引必须以 <c>approval_instance_id IS NOT NULL</c> 过滤，避免多行空值互相冲突。
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        // 商品条码租户内唯一：过滤空条码与软删行，避免空值/历史数据互相冲突
        _ = modelBuilder.Entity<Item>()
            .HasIndex(i => new { i.TenantId, i.Barcode })
            .IsUnique()
            .HasFilter("barcode IS NOT NULL AND is_deleted = 0");

        // 审批实例 Id 租户内唯一：仅对已回写实例 Id 的请求生效
        _ = modelBuilder.Entity<StkApprovalRequest>()
            .HasIndex(r => new { r.TenantId, r.ApprovalInstanceId })
            .IsUnique()
            .HasFilter("approval_instance_id IS NOT NULL");

        // 每仓库唯一已过账期初（status = 3 Posted）：过滤唯一索引兜底「一次性锁定」
        _ = modelBuilder.Entity<OpeningBatch>()
            .HasIndex(b => new { b.TenantId, b.WarehouseId })
            .IsUnique()
            .HasFilter("status = 3");

        // 台账并发令牌（long Version，非 rowversion，A-8）：事务内冲突即整单回滚重试
        _ = modelBuilder.Entity<StockBalance>()
            .Property(b => b.Version)
            .IsConcurrencyToken();

        // 流水幂等唯一索引（8 列）：来源类型+来源单号+行号+商品+仓库+批次+方向
        // 显式清除 SQL Server 对可空列自动追加的 IS NOT NULL 过滤：非批次商品（batch_no 为 null）
        // 同样必须受唯一约束保护，否则并发重投会写入重复流水（幂等去重的数据库级兜底不可失效）
        _ = modelBuilder.Entity<StockTxn>()
            .HasIndex(t => new
            {
                t.TenantId,
                t.SourceType,
                t.SourceDocNo,
                t.SourceLineNo,
                t.ItemId,
                t.WarehouseId,
                t.BatchNo,
                t.Direction,
            })
            .IsUnique()
            .HasFilter(null);

        // 所有 long 主键（含审计实体）挂雪花值生成器
        SnowflakeKeyConvention.Apply(modelBuilder, this);
    }

    /// <inheritdoc />
    public override int SaveChanges()
    {
        GuardStockTxnImmutable();
        return base.SaveChanges();
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        GuardStockTxnImmutable();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 流水只增守护（A-7）：代码层拦截 <c>StockTxn</c> 的更新与删除，
    /// 数据库侧另有触发器 <c>trg_stk_stock_txns_no_update_delete</c> 兜底。
    /// </summary>
    /// <exception cref="InvalidOperationException">存在被更新或删除的库存流水时抛出。</exception>
    private void GuardStockTxnImmutable()
    {
        foreach (EntityEntry<StockTxn> entry in ChangeTracker.Entries<StockTxn>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("库存流水只允许新增，禁止更新或删除");
            }
        }
    }
}
