/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 03:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 03:30:00 +08:00 中国标准时间 星期三
 * 描述：租户迁移计划接口，各业务模块实现此接口声明本模块 DbContext + 依赖
 */

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 租户迁移计划接口。**每个业务模块** 必须实现此接口，
/// 由模块 DI 注册扩展（AddQlx{Module}）内部自动挂载。
/// 典型实现：ModuleCode 返回 "pur"、DbContextType 返回 PurchaseDbContext、DependsOn 返回 ["bas"]。
/// </summary>
public interface ITenantMigrationPlan
{
    /// <summary>模块编码（必须与 ModuleDefinition.ModuleCode 一致）。</summary>
    string ModuleCode { get; }

    /// <summary>本模块租户 DbContext 类型（必须继承 TenantDbContext）。</summary>
    Type DbContextType { get; }

    /// <summary>
    /// 依赖的其它模块编码列表；这些模块的迁移必须在本模块之前完成。
    /// 为空表示无依赖，可最先执行。
    /// </summary>
    IReadOnlyList<string> DependsOn { get; }
}
