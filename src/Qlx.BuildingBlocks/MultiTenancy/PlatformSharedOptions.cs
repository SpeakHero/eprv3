/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 22:10:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 22:10:00 +08:00 中国标准时间 星期三
 * 描述：平台共享库配置（配置节 Qlx:PlatformShared），平台全局项只读通道与保留期参数
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 平台共享库配置。共享库只承载平台全局项（<c>glb_*</c>）与任务队列，
/// 租户侧通过只读连接读取；本期先落配置与校验，业务随 2D 信息中心落地。
/// </summary>
public sealed record PlatformSharedOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Qlx:PlatformShared";

    /// <summary>共享库主连接串（平台写入用）。</summary>
    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>共享库只读连接串（租户侧读 <c>glb_*</c> 用）；开发期可留空回退主连接。</summary>
    public string? ReadOnlyConnectionString { get; init; }

    /// <summary>全局消息保留天数（0 表示不限制）。</summary>
    [Range(0, 36500)]
    public int GlobalMessageRetentionDays { get; init; } = 365;

    /// <summary>租户收件箱读取共享库的缓存秒数（0 表示不缓存）。</summary>
    [Range(0, 86400)]
    public int InboxCacheSeconds { get; init; } = 30;

    /// <summary>实际使用的只读连接串：未配置时回退主连接串。</summary>
    public string EffectiveReadOnlyConnectionString =>
        string.IsNullOrWhiteSpace(ReadOnlyConnectionString) ? ConnectionString : ReadOnlyConnectionString;
}
