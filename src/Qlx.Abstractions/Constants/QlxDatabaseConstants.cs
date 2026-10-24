/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 20:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 20:30:00 +08:00 中国标准时间 星期四
 * 描述：数据库连接协议常量（SQL Server 默认端口、LocalDB 主机标记与默认实例名），全局唯一入口
 */

namespace Qlx.Abstractions.Constants;

/// <summary>
/// 全局统一的数据库连接协议常量。跨模块共享，禁止在 Options / 预配服务 / 连接串构造器中重复声明。
/// </summary>
public static class QlxDatabaseConstants
{
    /// <summary>SQL Server 默认端口。</summary>
    public const int SqlServerDefaultPort = 1433;

    /// <summary>LocalDB 主机标识（连接串 DataSource 前缀）。</summary>
    public const string LocalDbHostMarker = "(localdb)";

    /// <summary>LocalDB 默认实例名（未显式指定实例时补全）。</summary>
    public const string LocalDbDefaultInstance = "MSSQLLocalDB";
}
