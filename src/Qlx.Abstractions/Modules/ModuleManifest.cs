/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：模块清单，声明模块身份、表前缀、路由、菜单、权限点、任务与事件订阅
 */

using Qlx.Abstractions.Jobs;

namespace Qlx.Abstractions.Modules;

/// <summary>
/// 模块清单。每个业务/支撑模块必须提供一份不可变清单，
/// 供宿主装配路由、下发菜单、注册权限点、调度任务与激活事件订阅。
/// 许可停用模块时：路由返回 404、菜单不下发、任务暂停、事件订阅不激活。
/// </summary>
public sealed record ModuleManifest
{
    /// <summary>
    /// 模块编码，全局唯一（如 <c>pur</c>、<c>sal</c>），同时作为缓存键与权限点前缀。
    /// </summary>
    public required string ModuleCode { get; init; }

    /// <summary>
    /// 模块显示名。
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// 数据库表前缀（如 <c>pur_</c>），模块独立 DbContext 内所有表必须带此前缀。
    /// </summary>
    public required string TablePrefix { get; init; }

    /// <summary>
    /// 模块声明的权限点集合。
    /// </summary>
    public IReadOnlyList<PermissionDefinition> Permissions { get; init; } = [];

    /// <summary>
    /// 模块声明的菜单集合。
    /// </summary>
    public IReadOnlyList<MenuDefinition> Menus { get; init; } = [];

    /// <summary>
    /// 模块声明的内置后台任务集合（<see cref="Qlx.Abstractions.Jobs.JobDefinition"/>，含 CRON 调度）。
    /// </summary>
    public IReadOnlyList<JobDefinition> Jobs { get; init; } = [];

    /// <summary>
    /// 模块声明的服务任务集合（<see cref="Qlx.Abstractions.Jobs.ServiceJobDefinition"/>，无调度按需提交）。
    /// </summary>
    public IReadOnlyList<ServiceJobDefinition> ServiceJobs { get; init; } = [];

    /// <summary>
    /// 模块订阅的集成事件类型全名集合；用于许可停用时批量停用订阅。
    /// </summary>
    public IReadOnlyList<string> SubscribedEvents { get; init; } = [];

    /// <summary>
    /// 顶级导航分组集合。仅 Portal 模块需要声明（跨模块平台级配置）；
    /// 业务模块保持默认空数组即可。
    /// </summary>
    public IReadOnlyList<NavigationGroupDefinition> TopNavigationGroups { get; init; } = [];
}
