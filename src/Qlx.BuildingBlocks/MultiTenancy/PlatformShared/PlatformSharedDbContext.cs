/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 06:35:00 +08:00 中国标准时间 星期五
 * 描述：共享库 PlatformSharedDbContext——glb_* 全局项唯一物理承载（平台写、租户只读，禁止第五库红线）
 */

using Microsoft.EntityFrameworkCore;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.BuildingBlocks.MultiTenancy.PlatformShared;

/// <summary>
/// 共享库 DbContext。继承 <see cref="TenantDbContext"/> 以复用 snake_case 命名与迁移计划装配
/// （迁移执行器按 (resolver, tenantId, options) 反射创建）；共享库连接经
/// <see cref="SharedDatabaseResolver"/> 固定路由，无租户过滤（glb_* 为平台级数据，实体不含 TenantId）。
/// <para>平台侧发布/撤回为写入口（后续阶段）；本阶段租户侧只读。</para>
/// </summary>
public sealed partial class PlatformSharedDbContext(
    ITenantDatabaseResolver resolver,
    long tenantId,
    DbContextOptions options)
    : TenantDbContext(resolver, tenantId, options)
{
    /// <inheritdoc />
    protected override string? GetModuleTablePrefix() => null; // 前缀由实体 [TenantTablePrefix("glb_")] 声明
}
