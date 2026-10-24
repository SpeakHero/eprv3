/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-21 02:00:00 +08:00 中国标准时间 星期三
 * 描述：Data Protection 装配配置项，绑定 Qlx:DataProtection 节
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.BuildingBlocks.Security;

/// <summary>
/// Data Protection 配置项。配置节：<c>Qlx:DataProtection</c>。
/// </summary>
public sealed record DataProtectionOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "Qlx:DataProtection";

    /// <summary>
    /// 应用判别名，多应用共享密钥目录时必须区分，默认 <c>Qlx.Erpv3</c>。
    /// </summary>
    [Required]
    public string ApplicationName { get; init; } = "Qlx.Erpv3";

    /// <summary>
    /// 密钥环持久化目录。为空时不持久化到文件系统（密钥仅驻留内存，重启失效），
    /// 生产环境必须指向共享存储目录或替换为 Redis / 数据库持久化。
    /// </summary>
    public string? KeyDirectory { get; init; }

    /// <summary>
    /// 密钥有效期天数，默认 90 天。
    /// </summary>
    [Range(1, 3650)]
    public int KeyLifetimeDays { get; init; } = 90;
}
