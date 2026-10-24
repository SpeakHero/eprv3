/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21
 * 修改时间：2026-10-22 20:40:00 +08:00 中国标准时间 星期四
 * 描述：Bas（基础信息）模块租户级 DbContext，表前缀 bas_，继承 TenantDbContext 自动路由租户库 + 全局 TenantId 过滤
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.Modules.Bas;

/// <summary>
/// Bas 模块租户 DbContext。
/// 构造由 ServiceCollection 工厂注册，动态注入当前请求的 tenantId。
/// 实体映射统一由实体数据注解声明（<c>[MaxLength]</c> / <c>[Index]</c> / <c>[PrimaryKey]</c>），
/// 表名与列名 snake_case 由 <see cref="TenantDbContext"/> 基类统一转换，主文件不再承载逐实体配置。
/// </summary>
public sealed partial class BasTenantDbContext(
    ITenantDatabaseResolver resolver,
    long tenantId,
    DbContextOptions options)
    : TenantDbContext(resolver, tenantId, options)
{
    /// <inheritdoc />
    protected override string? GetModuleTablePrefix() => QlxModuleCodes.BasTablePrefix;
}
