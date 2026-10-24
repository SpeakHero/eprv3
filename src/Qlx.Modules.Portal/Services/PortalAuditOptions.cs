/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 22:10:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 22:10:00 +08:00 中国标准时间 星期三
 * 描述：门户审计配置（配置节 Qlx:Portal:Audit），审计并库后的保留期参数
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 门户审计配置。审计日志并入门户库 <c>plog_*</c> 后，按保留期清理。
/// </summary>
public sealed record PortalAuditOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Qlx:Portal:Audit";

    /// <summary>审计/操作日志保留天数。</summary>
    [Range(1, 36500)]
    public int RetentionDays { get; init; } = 90;
}
