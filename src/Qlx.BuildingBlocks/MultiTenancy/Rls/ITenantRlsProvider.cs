/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 01:15:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 01:15:00 +08:00 中国标准时间 星期三
 * 描述：租户行级安全提供程序抽象，按数据库类型实现会话上下文与安全策略脚本
 */

using System.Data.Common;

namespace Qlx.BuildingBlocks.MultiTenancy.Rls;

/// <summary>
/// 租户行级安全提供程序（provider 无关抽象）。
/// 由具体数据库实现：SQL Server 使用 <c>SESSION_CONTEXT</c> + Security Policy；
/// PostgreSQL / Oracle 待实现；MySQL 无原生 RLS 返回 <see cref="RlsCapability.Unsupported"/>。
/// </summary>
public interface ITenantRlsProvider
{
    /// <summary>数据库提供程序名称（用于日志与巡检报告）。</summary>
    string ProviderName { get; }

    /// <summary>RLS 能力标记；<see cref="RlsCapability.Unsupported"/> 时其余方法均为空操作。</summary>
    RlsCapability Capability { get; }

    /// <summary>
    /// 在已打开的连接上写入当前租户会话上下文。连接池复用连接时必须每次重写。
    /// </summary>
    /// <param name="connection">数据库连接。</param>
    /// <param name="tenantId">当前租户 ID（必须大于 0）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SetSessionTenantAsync(DbConnection connection, long tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// 生成 RLS 安装脚本（幂等，可重复执行）：谓词函数 + 每表安全策略。
    /// </summary>
    /// <param name="tables">需要安装策略的租户表。</param>
    /// <returns>按顺序执行的 SQL 语句集合；不支持 RLS 时为空集合。</returns>
    IReadOnlyList<string> BuildInstallStatements(IReadOnlyCollection<TenantRlsTable> tables);
}
