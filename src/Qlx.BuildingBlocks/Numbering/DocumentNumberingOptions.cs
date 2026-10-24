/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-21 02:00:00 +08:00 中国标准时间 星期三
 * 描述：单据编号生成配置项，绑定 Qlx:Numbering 节
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.BuildingBlocks.Numbering;

/// <summary>
/// 单据编号生成配置项。配置节：<c>Qlx:Numbering</c>。
/// </summary>
public sealed record DocumentNumberingOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "Qlx:Numbering";

    /// <summary>
    /// 期间格式（<see cref="DateTimeOffset.ToString(string)"/> 格式串），默认 <c>yyyyMM</c>。
    /// 修改格式即改变流水重置周期，变更需评估历史编号区间。
    /// </summary>
    [Required]
    public string PeriodFormat { get; init; } = "yyyyMM";

    /// <summary>
    /// 流水号补齐位数，默认 6 位（000001 起）。
    /// </summary>
    [Range(1, 18)]
    public int SerialPadding { get; init; } = 6;

    /// <summary>
    /// 各段之间的分隔符，默认 <c>-</c>。
    /// </summary>
    [Required]
    public string Separator { get; init; } = "-";
}
