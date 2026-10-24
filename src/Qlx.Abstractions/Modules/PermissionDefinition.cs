/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：权限点定义，模块经 Manifest 声明，供许可过滤与鉴权统一消费
 */

namespace Qlx.Abstractions.Modules;

/// <summary>
/// 权限点类型。
/// </summary>
public enum PermissionKind
{
    /// <summary>菜单级权限。</summary>
    Menu,

    /// <summary>按钮 / 操作级权限。</summary>
    Action,

    /// <summary>数据行级权限。</summary>
    Data,

    /// <summary>字段级权限。</summary>
    Field,
}

/// <summary>
/// 权限点定义。编码全局唯一，约定格式：<c>模块码.资源.操作</c>（如 pur.order.approve）。
/// </summary>
/// <param name="Code">权限点编码。</param>
/// <param name="Name">权限点显示名。</param>
/// <param name="Kind">权限点类型。</param>
public sealed record PermissionDefinition(string Code, string Name, PermissionKind Kind);
