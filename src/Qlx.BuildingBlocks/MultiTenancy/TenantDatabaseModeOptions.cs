/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 22:10:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 22:10:00 +08:00 中国标准时间 星期三
 * 描述：租户数据库模式默认值与预留套餐映射（配置节 Qlx:Tenancy:DatabaseMode）
 */

using Qlx.Abstractions.Tenancy;

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 租户数据库模式配置。本期只使用 <see cref="DefaultMode"/>；
/// <see cref="PlanMappings"/> 为会员体系预留，本期不参与任何判定。
/// </summary>
public sealed record TenantDatabaseModeOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Qlx:Tenancy:DatabaseMode";

    /// <summary>新建租户未显式选择时的默认模式。</summary>
    public TenantDatabaseMode DefaultMode { get; init; } = TenantDatabaseMode.Shared;

    /// <summary>
    /// 预留的套餐 → 模式映射（会员等级仅产出建议，不自动切换）。
    /// 本期为空且不读取。
    /// </summary>
    public IReadOnlyDictionary<string, TenantDatabaseMode>? PlanMappings { get; init; }
}
