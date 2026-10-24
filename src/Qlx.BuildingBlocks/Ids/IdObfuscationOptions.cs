/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-21 02:00:00 +08:00 中国标准时间 星期三
 * 描述：ID 混淆配置项，盐等敏感值在配置中只存引用、经 Data Protection 保护
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.BuildingBlocks.Ids;

/// <summary>
/// ID 混淆配置项。对应配置节 <c>Qlx:Ids:Obfuscation</c>。
/// </summary>
public sealed record IdObfuscationOptions
{
    /// <summary>
    /// 配置节名。
    /// </summary>
    public const string SectionName = "Qlx:Ids:Obfuscation";

    /// <summary>
    /// 混淆盐。生产环境必须经密钥引用注入，禁止在仓库配置文件中明文存放。
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Salt { get; init; } = string.Empty;

    /// <summary>
    /// 混淆字符串最小长度。
    /// </summary>
    [Range(0, 64)]
    public int MinLength { get; init; } = 8;

    /// <summary>
    /// 自定义字母表；为空时使用默认字母表。至少 16 个互不重复的字符。
    /// </summary>
    public string? Alphabet { get; init; }
}
