/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 00:10:00 +08:00 中国标准时间 星期五
 * 描述：文件任务处理器共享 ACL 计算守护（批量加载对象链 + 条目 + 主体，调 FileAclEngine 纯函数）
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Files;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Files;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Support.Domain;

namespace Qlx.Modules.Support.Jobs.Files;

/// <summary>
/// ACL 计算守护。处理器内部专用：一次批量加载目标对象沿祖先链的 ACL 条目与当前用户主体，
/// 交由 <see cref="FileAclEngine"/> 计算（避免 N+1）。
/// </summary>
internal static class FileAclGuard
{
    /// <summary>
    /// 计算操作者对目标对象的当前有效权限。
    /// </summary>
    /// <param name="db">租户 DbContext（已绑定租户）。</param>
    /// <param name="subjectQuery">租户主体查询（未注册时按普通用户、无角色处理）。</param>
    /// <param name="target">目标对象。</param>
    /// <param name="operatorId">操作人用户标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>有效权限位。</returns>
    public static async Task<FilePermission> ComputeAsync(
        SupportTenantDbContext db,
        ITenantSubjectQuery? subjectQuery,
        StorageObject target,
        string operatorId,
        CancellationToken cancellationToken)
    {
        var entries = await LoadEntriesAsync(db, target, cancellationToken).ConfigureAwait(false);
        var subjects = await ResolveSubjectsAsync(subjectQuery, db.CurrentTenantId, operatorId, cancellationToken)
            .ConfigureAwait(false);

        return FileAclEngine.ComputeEffective(
            entries, target.Id, target.OwnerId, subjects, operatorId, subjects.IsTenantAdmin, target.InheritAcl);
    }

    /// <summary>
    /// 批量加载目标对象沿祖先链的 ACL 条目并投影为引擎入参（一次 2 条 SQL：祖先对象 + 条目，避免 N+1）。
    /// </summary>
    public static async Task<IReadOnlyList<AclEngineEntry>> LoadEntriesAsync(
        SupportTenantDbContext db, StorageObject target, CancellationToken cancellationToken)
    {
        var ancestorPaths = BuildAncestorPaths(target.Path);
        var ancestors = ancestorPaths.Count == 0
            ? []
            : await db.StorageObjects
                .AsNoTracking()
                .Where(o => ancestorPaths.Contains(o.Path))
                .Select(o => new { o.Id, o.Path })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var objectIds = new List<long> { target.Id };
        objectIds.AddRange(ancestors.Select(static a => a.Id));

        var entries = await db.FileAclEntries
            .AsNoTracking()
            .Where(e => objectIds.Contains(e.ObjectId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pathById = ancestors.ToDictionary(static a => a.Id, static a => a.Path);
        return entries
            .Select(e => new AclEngineEntry(
                e.ObjectId,
                pathById.GetValueOrDefault(e.ObjectId, target.Path),
                e.SubjectType,
                e.SubjectId,
                (FilePermission)e.PermMask,
                (AceType)e.AceType,
                e.Propagate))
            .ToList();
    }

    /// <summary>解析当前操作者的租户主体（用户 Id 非数字时视为匿名主体；供回收站管理员判定等复用）。</summary>
    internal static async Task<TenantSubjects> ResolveSubjectsAsync(
        ITenantSubjectQuery? subjectQuery, long tenantId, string operatorId, CancellationToken cancellationToken)
    {
        if (subjectQuery is null
            || !long.TryParse(operatorId, System.Globalization.CultureInfo.InvariantCulture, out var portalUserId)
            || portalUserId <= 0)
        {
            return new TenantSubjects([], [], [], IsTenantAdmin: false);
        }

        return await subjectQuery
            .GetSubjectsAsync(GuidIdMapper.ToGuid(tenantId), GuidIdMapper.ToGuid(portalUserId), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>按物化路径展开祖先链（不含自身），如 /purchase/2026/10 → /purchase、/purchase/2026。</summary>
    private static List<string> BuildAncestorPaths(string path)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(path) || path[0] != '/')
        {
            return result;
        }

        var index = path.IndexOf('/', 1);
        while (index > 0)
        {
            result.Add(path[..index]);
            index = path.IndexOf('/', index + 1);
        }

        return result;
    }
}
