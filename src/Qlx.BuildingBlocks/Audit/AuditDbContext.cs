/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 00:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 20:40:00 +08:00 中国标准时间 星期四
 * 描述：审计日志 DbContext（B4 后并入 qlx_portal 门户库，表前缀 plog_，不继承 TenantDbContext）
 */

using Microsoft.EntityFrameworkCore;

namespace Qlx.BuildingBlocks.Audit;

/// <summary>
/// 审计日志 DbContext（B4 后并入 qlx_portal 门户库，表前缀 <c>plog_</c>）。
/// 不继承 TenantDbContext（AuditSaveChangesInterceptor 在 TenantDbContext 内运行，
/// 若此处继承会造成构造期循环依赖）。迁移历史表独立为 <c>__EFMigrationsHistory_audit</c>，
/// 与门户库 <c>PortalDbContext</c> 的迁移历史表并存。
/// <para>实体映射全部由数据注解声明（表名 / 列名 / 长度 / 索引），本文件仅保留上下文构造。</para>
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
/// <param name="options">DbContextOptions，由宿主配置固定连接串注入。</param>
public sealed partial class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
}
