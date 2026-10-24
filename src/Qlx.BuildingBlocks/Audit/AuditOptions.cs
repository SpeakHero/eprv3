/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 02:00:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 02:00:00 +08:00 中国标准时间 星期三
 * 描述：审计库连接配置项，强类型 Options（禁止裸读 IConfiguration 字符串键）
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.BuildingBlocks.Audit;

/// <summary>
/// 审计库配置项。对应配置节 <c>Qlx:Audit</c>。
/// 强类型 Options + ValidateOnStart，禁止裸读 <c>IConfiguration["..."]</c> 字符串键。
/// </summary>
public sealed record AuditOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Qlx:Audit";

    /// <summary>
    /// 审计库固定连接串。开发环境可直接写 LocalDB（qlx_dev / qlx_audit），
    /// 生产环境只存密钥引用（经 Data Protection 解密注入真实值）。
    /// </summary>
    [Required]
    public string ConnectionString { get; init; } = string.Empty;
}
