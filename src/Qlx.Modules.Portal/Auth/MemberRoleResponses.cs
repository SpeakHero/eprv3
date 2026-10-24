/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 23:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 23:30:00 +08:00 中国标准时间 星期四
 * 描述：选择角色步骤响应 DTO
 */

namespace Qlx.Modules.Portal.Auth;

/// <summary>选择角色步骤的角色选项（RoleId 为 Hashids 混淆串）。</summary>
/// <param name="RoleId">角色 Id（对外混淆串）。</param>
/// <param name="RoleCode">角色编码。</param>
/// <param name="RoleName">角色名称。</param>
/// <param name="IsBuiltin">是否系统内置角色（前端展示「内置」标签）。</param>
public sealed record MemberRoleOptionItem(
    string RoleId,
    string RoleCode,
    string RoleName,
    bool IsBuiltin);
