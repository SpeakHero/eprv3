/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 23:20:00 +08:00 中国标准时间 星期四
 * 描述：Support（支撑服务中心）模块租户级 DbContext，默认前缀 fil_，实体可用 [TenantTablePrefix] 覆盖（job_/msg_/apr_/prt_）
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.Modules.Support;

/// <summary>
/// Support 模块租户 DbContext。构造由 ServiceCollection 工厂注册，动态注入当前请求的 tenantId。
/// 2B 仅承载文件域 fil_ 表；2C 起的 job_/msg_/apr_/prt_ 实体以 <c>[TenantTablePrefix]</c> 特性声明各自前缀。
/// 实体映射统一由数据注解声明（[MaxLength] / [Index] / [PrimaryKey] / [TenantTablePrefix]）。
/// </summary>
public sealed partial class SupportTenantDbContext(
    ITenantDatabaseResolver resolver,
    long tenantId,
    DbContextOptions options)
    : TenantDbContext(resolver, tenantId, options)
{
    /// <inheritdoc />
    protected override string? GetModuleTablePrefix() => QlxModuleCodes.SupportFileTablePrefix;

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 雪花主键：全部 long 主键挂显式雪花值生成器（Add 即得真实值），禁止数据库 IDENTITY
        SnowflakeKeyConvention.Apply(modelBuilder, this);
    }
}
