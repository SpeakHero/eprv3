/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 20:40:00 +08:00 中国标准时间 星期四
 * 描述：TenantUpgradeJob 实体 Fluent 配置：同租户活跃任务唯一（SQL Server 过滤索引）
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qlx.Modules.Portal.Domain.Tenancy;

namespace Qlx.Modules.Portal.Configurations;

/// <summary>
/// 租户升级任务实体映射配置。表名、长度约束与常规索引已由数据注解声明；
/// 「同租户同时只允许一个进行中任务」的过滤唯一索引无法用数据注解表达，故在此声明。
/// </summary>
public sealed class TenantUpgradeJobConfiguration : IEntityTypeConfiguration<TenantUpgradeJob>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TenantUpgradeJob> builder)
    {
        // 同租户同时只允许一个进行中任务（Done=5/Failed=6/Cleaned=7 允许历史多行）；
        // SQL Server 过滤索引不支持 NOT IN，活跃状态恰为 0..4，用 < 5 等价表达；
        // Failed 未重试时由应用层 AnyAsync 拦截，索引只兜底真正活跃的并发登记
        builder.HasIndex(j => j.TenantId).IsUnique().HasFilter("[Status] < 5");
    }
}
