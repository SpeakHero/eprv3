/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-21 02:00:00 +08:00 中国标准时间 星期三
 * 描述：缓存配置项，provider 节整体切换 Redis / 内存提供方
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.BuildingBlocks.Caching;

/// <summary>
/// 缓存配置项。对应配置节 <c>Qlx:Cache</c>。
/// 修改 <see cref="Provider"/> 即可整体替换提供方，业务代码零改动。
/// </summary>
public sealed record CacheOptions
{
    /// <summary>
    /// 配置节名。
    /// </summary>
    public const string SectionName = "Qlx:Cache";

    /// <summary>缓存提供方取值常量（配置值大小写不敏感）。</summary>
    public static class Providers
    {
        /// <summary>Redis 提供方（生产多实例默认）。</summary>
        public const string Redis = "Redis";

        /// <summary>进程内内存提供方（开发 / 单实例）。</summary>
        public const string Memory = "Memory";
    }

    /// <summary>
    /// 缓存提供方：<c>Memory</c>（进程内内存，开发/单实例）或 <c>Redis</c>（默认，多实例）。
    /// </summary>
    [Required]
    public string Provider { get; init; } = Providers.Redis;

    /// <summary>
    /// 实例名，作为所有物理键的统一前缀，避免多系统共库冲突。
    /// </summary>
    [Required]
    public string InstanceName { get; init; } = "qlx";

    /// <summary>
    /// 默认过期时长（秒）；业务未显式指定过期时生效。
    /// </summary>
    [Range(1, 864000)]
    public int DefaultExpirationSeconds { get; init; } = 1800;

    /// <summary>
    /// Redis 连接串；<see cref="Provider"/> 为 <c>Redis</c> 时必填，经密钥引用注入。
    /// </summary>
    public string? RedisConnectionString { get; init; }
}
