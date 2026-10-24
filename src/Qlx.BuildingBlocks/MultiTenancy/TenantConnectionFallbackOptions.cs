/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 23:59:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 23:59:00 +08:00 中国标准时间 星期三
 * 描述：租户 Id 未初始化时的开发兜底连接配置（配置节 Qlx:Tenancy:DevelopmentFallback）
 */

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 租户 Id 未初始化（<c>TenantId=0</c>）时的开发兜底连接配置。
/// 默认关闭；仅开发环境在配置中显式 <c>Enabled=true</c> 并给出连接串时生效，
/// 生产环境不得启用（未启用时直接抛出异常，避免误连任意库）。
/// </summary>
public sealed record TenantConnectionFallbackOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Qlx:Tenancy:DevelopmentFallback";

    /// <summary>是否启用兜底。默认 <c>false</c>（生产不启用）。</summary>
    public bool Enabled { get; init; }

    /// <summary>兜底连接串（仅 <see cref="Enabled"/> 为 <c>true</c> 时生效）。</summary>
    public string? ConnectionString { get; init; }
}
