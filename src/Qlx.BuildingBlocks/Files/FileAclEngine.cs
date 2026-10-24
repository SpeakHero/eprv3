/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 23:45:00 +08:00 中国标准时间 星期四
 * 描述：文件 ACL 计算引擎（纯函数）：DENY 优先 → ALLOW 并集 → 默认拒绝；支持继承链展开
 */

using Qlx.Abstractions.Files;
using Qlx.Abstractions.Tenancy;

namespace Qlx.BuildingBlocks.Files;

/// <summary>
/// ACL 计算引擎输入条目（实体无关的扁平快照，任务处理器由 fil_acl_entries 投影而来）。
/// </summary>
/// <param name="ObjectId">所属对象 Id。</param>
/// <param name="ObjectPath">所属对象物化路径（继承链匹配用）。</param>
/// <param name="SubjectType">主体类型。</param>
/// <param name="SubjectId">主体 Id；<c>*</c> 表示全部主体。</param>
/// <param name="Permission">权限位。</param>
/// <param name="AceType">Allow/Deny。</param>
/// <param name="Propagate">是否向子级传播。</param>
public sealed record AclEngineEntry(
    long ObjectId,
    string ObjectPath,
    FileSubjectType SubjectType,
    string SubjectId,
    FilePermission Permission,
    AceType AceType,
    bool Propagate);

/// <summary>ACL 引擎 ACE 类型（与实体枚举解耦的纯函数入参形态）。</summary>
public enum AceType
{
    /// <summary>允许。</summary>
    Allow = 1,

    /// <summary>拒绝（优先于 Allow）。</summary>
    Deny = 2,
}

/// <summary>
/// 文件 ACL 计算引擎（纯静态函数，无副作用）。
/// 计算规则（《04-文件存储中心》R5）：
/// ① 所有者与租户管理员恒为 All；② 沿对象路径展开祖先继承链（InheritAcl 时生效，由调用方合并条目），
/// 仅纳入沿祖先链 Propagate=true 的条目；③ 命中任一 DENY（按主体匹配）→ 整体拒绝该权限位；
/// ④ 否则 ALLOW 按主体并集；⑤ 无命中 → 默认拒绝。
/// </summary>
public static class FileAclEngine
{
    /// <summary>通配主体 Id（表示全部用户/角色/组织）。</summary>
    public const string WildcardSubjectId = "*";

    /// <summary>
    /// 计算指定主体对目标对象的有效权限。
    /// </summary>
    /// <param name="entries">候选条目集合（含对象自身与其全部祖先的条目快照）。</param>
    /// <param name="targetObjectId">目标对象 Id。</param>
    /// <param name="ownerId">对象所有者用户标识。</param>
    /// <param name="subjects">当前用户主体聚合（角色/组织/成员 Id 列表）。</param>
    /// <param name="currentUserId">当前用户标识。</param>
    /// <param name="isTenantAdmin">当前用户是否租户管理员。</param>
    /// <param name="inheritsFromParent">目标对象是否继承父级 ACL（InheritAcl 开关）。</param>
    /// <returns>有效权限位。</returns>
    public static FilePermission ComputeEffective(
        IReadOnlyList<AclEngineEntry> entries,
        long targetObjectId,
        string ownerId,
        TenantSubjects subjects,
        string currentUserId,
        bool isTenantAdmin,
        bool inheritsFromParent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(subjects);

        if (isTenantAdmin || (!string.IsNullOrEmpty(currentUserId) && string.Equals(currentUserId, ownerId, StringComparison.Ordinal)))
        {
            return FilePermission.All;
        }

        // 继承链展开：目标对象自身的条目恒生效；祖先条目仅当祖先条目 Propagate=true 且目标继承开关开启时生效
        var applicable = entries
            .Where(e => inheritsFromParent || e.ObjectId == targetObjectId)
            .Where(e => e.ObjectId == targetObjectId || e.Propagate)
            .ToArray();

        var denyMask = FilePermission.None;
        var allowMask = FilePermission.None;

        foreach (var entry in applicable)
        {
            if (!SubjectMatches(entry, currentUserId, subjects))
            {
                continue;
            }

            if (entry.AceType == AceType.Deny)
            {
                denyMask |= entry.Permission;
            }
            else
            {
                allowMask |= entry.Permission;
            }
        }

        // 规则：DENY 优先 → ALLOW 并集 → 默认拒绝
        return allowMask & ~denyMask;
    }

    /// <summary>判断条目主体是否匹配当前用户（通配 / 用户相等 / 命中任一角色或组织 Id）。</summary>
    private static bool SubjectMatches(AclEngineEntry entry, string currentUserId, TenantSubjects subjects)
    {
        if (string.Equals(entry.SubjectId, WildcardSubjectId, StringComparison.Ordinal))
        {
            return true;
        }

        return entry.SubjectType switch
        {
            FileSubjectType.User => !string.IsNullOrEmpty(currentUserId)
                && string.Equals(entry.SubjectId, currentUserId, StringComparison.Ordinal),
            FileSubjectType.Role => subjects.RoleIds.Contains(entry.SubjectId, StringComparer.Ordinal),
            FileSubjectType.Org => subjects.OrgIds.Contains(entry.SubjectId, StringComparer.Ordinal),
            _ => false,
        };
    }
}
