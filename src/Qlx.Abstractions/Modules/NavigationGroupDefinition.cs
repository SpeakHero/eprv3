/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 22:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 22:00:00 +08:00 中国标准时间 星期四
 * 描述：顶级导航分组定义，跨模块平台级配置，由 Portal 模块 Manifest 声明
 */

namespace Qlx.Abstractions.Modules;

/// <summary>
/// 顶级导航分组定义（跨模块平台级配置）。
/// 门户管理模块 Manifest 的 TopNavigationGroups 属性专用；
/// 业务模块无需声明，保持默认空数组即可。
/// </summary>
/// <param name="Code">分组编码（如 workbench / business / finance / people / basedata / system）。</param>
/// <param name="Name">分组显示名。</param>
/// <param name="ModuleCodes">该分组包含的模块编码集合（逗号分隔字符串）。</param>
/// <param name="SortOrder">同级排序号。</param>
public sealed record NavigationGroupDefinition(
    string Code,
    string Name,
    string ModuleCodes,
    int SortOrder);
