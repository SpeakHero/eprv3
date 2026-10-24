/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-21 02:00:00 +08:00 中国标准时间 星期三
 * 描述：雪花 ID 相关配置项，绑定 config 中 Qlx:Ids:Snowflake 节
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.BuildingBlocks.Ids;

/// <summary>
/// 雪花 ID 配置项。对应配置节 <c>Qlx:Ids:Snowflake</c>。
/// </summary>
public sealed record SnowflakeOptions
{
    /// <summary>
    /// 配置节名。
    /// </summary>
    public const string SectionName = "Qlx:Ids:Snowflake";

    /// <summary>workerId 租约提供方取值常量（配置值大小写不敏感）。</summary>
    public static class LeaseProviders
    {
        /// <summary>静态 workerId（开发 / 单实例）。</summary>
        public const string Static = "Static";

        /// <summary>Redis 租约（生产多实例）。</summary>
        public const string Redis = "Redis";
    }

    /// <summary>
    /// workerId 租约提供方：<c>Static</c>（静态配置，开发/单实例）或 <c>Redis</c>（生产多实例）。
    /// </summary>
    [Required]
    public string LeaseProvider { get; init; } = LeaseProviders.Static;

    /// <summary>
    /// 静态 workerId，仅 <see cref="LeaseProvider"/> 为 <c>Static</c> 时生效。
    /// </summary>
    [Range(0, SnowflakeIdGenerator.MaxWorkerId)]
    public long StaticWorkerId { get; init; }

    /// <summary>
    /// 自定义纪元（ISO-8601）；为空时使用 2026-01-01 UTC。
    /// </summary>
    public DateTimeOffset? Epoch { get; init; }

    /// <summary>
    /// Redis 租约键前缀。
    /// </summary>
    public string RedisKeyPrefix { get; init; } = "qlx:snowflake:worker";

    /// <summary>
    /// Redis 租约有效期（秒）。
    /// </summary>
    [Range(5, 600)]
    public int LeaseSeconds { get; init; } = 30;

    /// <summary>
    /// Redis 租约续期间隔（秒），必须小于 <see cref="LeaseSeconds"/>。
    /// </summary>
    [Range(1, 300)]
    public int RenewIntervalSeconds { get; init; } = 10;
}
