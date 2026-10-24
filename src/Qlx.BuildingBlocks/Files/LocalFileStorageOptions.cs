/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 23:35:00 +08:00 中国标准时间 星期四
 * 描述：本地文件存储后端 Options（根目录来自 SupportOptions.Files.LocalStorageRoot）
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.BuildingBlocks.Files;

/// <summary>
/// 本地文件存储后端配置。
/// </summary>
public sealed record LocalFileStorageOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Qlx:Support:Files";

    /// <summary>本地存储根目录（相对宿主内容根或绝对路径）。</summary>
    [Required(AllowEmptyStrings = false)]
    public string Root { get; init; } = string.Empty;
}
