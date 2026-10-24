/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 20:40:00 +08:00 中国标准时间 星期四
 * 描述：AuditDbContext 的 DbSet 集合
 */

using Microsoft.EntityFrameworkCore;

namespace Qlx.BuildingBlocks.Audit;

public sealed partial class AuditDbContext
{
    /// <summary>实体变更审计表（跨租户集中存储）。</summary>
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();

    /// <summary>业务级操作日志表。</summary>
    public DbSet<OperationLogEntity> OperationLogs => Set<OperationLogEntity>();
}
