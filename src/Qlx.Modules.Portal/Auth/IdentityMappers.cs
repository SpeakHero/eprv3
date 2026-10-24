/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 描述：Mapperly 编译期映射器：租户成员投影行 → 租户摘要 DTO（ID 混淆在映射边界完成）
 */

using Riok.Mapperly.Abstractions;
using Qlx.BuildingBlocks.Ids;

namespace Qlx.Modules.Portal.Auth;

/// <summary>
/// 身份响应映射器（Mapperly 源生成，零运行时反射）。
/// 实例化以注入 <see cref="IIdObfuscator"/>；ID 混淆是系统边界行为，
/// 因此 long → 混淆串的转换作为映射方法定义在此（Mapperly 自动识别用户实现的转换方法）。
/// </summary>
[Mapper]
public sealed partial class IdentityMappers(IIdObfuscator obfuscator)
{
    /// <summary>投影行 → 对外租户摘要（TenantId 混淆、角色转字符串）。</summary>
    /// <param name="row">查询投影行。</param>
    /// <returns>租户摘要 DTO。</returns>
    public partial TenantSummaryItem ToTenantSummaryItem(TenantMemberRow row);

    /// <summary>ID 混淆转换方法（Mapperly 识别为 long → string 的用户实现转换）。</summary>
    private string ObfuscateId(long id) => obfuscator.Encode(id);

    /// <summary>角色枚举转字符串（Mapperly 识别为枚举 → string 的用户实现转换）。</summary>
    private static string RoleToString(Domain.Identity.TenantMemberRole role) => role.ToString();
}
