/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 20:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 20:00:00 +08:00 中国标准时间 星期四
 * 描述：审计日志公共常量（模块编码、系统操作人、目标类型），全局唯一入口
 */

namespace Qlx.Abstractions.Constants;

/// <summary>
/// 全局统一的审计日志公共常量。
/// 审计记录的模块编码、系统操作人、目标实体类型跨模块共享，必须从此类引用。
/// </summary>
public static class QlxAuditConstants
{
    /// <summary>平台侧审计记录的模块编码（门户库 <c>plog_operation</c>）。</summary>
    public const string PortalModuleCode = QlxModuleCodes.Portal;

    /// <summary>系统自动操作的操作人标识（后台任务 / 迁移 / 种子数据）。</summary>
    public const string SystemOperator = "system";

    /// <summary>审计目标类型：租户。</summary>
    public const string TargetTenant = "Tenant";

    /// <summary>审计目标类型：门户用户。</summary>
    public const string TargetPortalUser = "PortalUser";

    /// <summary>审计目标类型：门户角色。</summary>
    public const string TargetPortalRole = "PortalRole";

    /// <summary>门户管理员操作人兜底显示名。</summary>
    public const string PortalAdminOperatorName = "门户管理员";
}
