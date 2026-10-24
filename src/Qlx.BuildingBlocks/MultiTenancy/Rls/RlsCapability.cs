/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 01:15:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 01:15:00 +08:00 中国标准时间 星期三
 * 描述：租户行级安全（RLS）能力标记与目标表描述
 */

namespace Qlx.BuildingBlocks.MultiTenancy.Rls;

/// <summary>
/// 数据库行级安全能力标记。三层隔离的 L2 依赖数据库原生能力，
/// 不支持 RLS 的数据库降级为 L1（EF 过滤 + 写入守卫）+ L3（巡检）。
/// </summary>
public enum RlsCapability
{
    /// <summary>不支持 RLS：仅启用 L1 + L3，并建议该租户使用独立库。</summary>
    Unsupported = 0,

    /// <summary>支持原生 RLS：可生成并安装安全策略。</summary>
    Supported = 1,
}

/// <summary>
/// RLS 策略目标表（每个含租户列的业务表一条）。
/// </summary>
/// <param name="Schema">数据库架构名（SQL Server 默认 dbo）。</param>
/// <param name="TableName">物理表名（snake_case，含模块前缀）。</param>
/// <param name="TenantColumn">租户列名（snake_case）。</param>
public sealed record TenantRlsTable(string Schema, string TableName, string TenantColumn)
{
    /// <summary>限定的表名（<c>schema.table</c>）。</summary>
    public string QualifiedName => $"{Schema}.{TableName}";
}
