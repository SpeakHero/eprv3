/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21
 * 描述：跨模块租户成员只读查询实现——从 Portal 库 por_tenant_members + por_users 跨表投影，供业务模块消费。
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Queries;
using Qlx.Modules.Portal.Domain.Identity;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// <see cref="ITenantMemberQuery"/> 实现，只读无副作用。
/// 通过 <see cref="IDbContextFactory{PortalDbContext}"/> 获取门户库 DbContext；
/// 消费方（如 Bas 模块）只需依赖 Abstractions 契约，禁止跨模块引用 Portal 类型。
/// </summary>
internal sealed class TenantMemberQuery(
    IDbContextFactory<PortalDbContext> dbFactory) : ITenantMemberQuery
{
    public async Task<bool> IsMemberExistsAsync(
        long tenantId, string account, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await (
            from m in db.Set<TenantMember>()
            join u in db.Set<PortalUser>() on m.PortalUserId equals u.Id
            where m.TenantId == tenantId && u.LoginAccount == account
            select m.Id)
            .AnyAsync(cancellationToken);
    }

    public async Task<PortalUserPreview?> FindPortalUserByAccountAsync(
        string account, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Set<PortalUser>()
            .Where(u => u.LoginAccount == account)
            .Select(u => new PortalUserPreview(
                u.Id,
                u.LoginAccount,
                u.DisplayName,
                u.Phone,
                u.Email))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<TenantMemberSummaryRow> Items, int Total)> QueryMembersAsync(
        long tenantId, string? keyword, long? orgId, int? role, int? status,
        int skip, int take, CancellationToken cancellationToken,
        IReadOnlyCollection<long>? orgIds = null, long? onlyMemberId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        // Count 用 Where 链（不 Select 元组），EF Core 不支持表达式树里的元组字面量
        IQueryable<TenantMember> baseFilter = db.Set<TenantMember>().Where(m => m.TenantId == tenantId);
        if (orgId.HasValue) baseFilter = baseFilter.Where(m => m.OrgId == orgId.Value);
        if (role.HasValue) baseFilter = baseFilter.Where(m => (int)m.Role == role.Value);
        if (status.HasValue) baseFilter = baseFilter.Where(m => (int)m.Status == status.Value);
        // X-5 数据范围过滤：组织 Id 集 / 仅本人（由 Bas 消费方按角色 DefaultDataScope 展开传入）
        if (orgIds is not null) baseFilter = baseFilter.Where(m => m.OrgId.HasValue && orgIds.Contains(m.OrgId.Value));
        if (onlyMemberId.HasValue) baseFilter = baseFilter.Where(m => m.Id == onlyMemberId.Value);

        IQueryable<TenantMember> countQuery = baseFilter;
        IQueryable<TenantMember> listQuery = baseFilter;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            // 含关键字的条件需要 Join PortalUser 才能过滤——Count 和 List 各自写一次 Where
            countQuery =
                from m in countQuery
                join u in db.Set<PortalUser>() on m.PortalUserId equals u.Id
                where (m.DisplayNameInTenant != null && m.DisplayNameInTenant.Contains(keyword))
                    || u.LoginAccount.Contains(keyword)
                    || (u.Phone != null && u.Phone.Contains(keyword))
                    || (u.Email != null && u.Email.Contains(keyword))
                select m;
        }

        var total = await countQuery.CountAsync(cancellationToken);

        var items = await (
            from m in listQuery
            join u in db.Set<PortalUser>() on m.PortalUserId equals u.Id
            where string.IsNullOrWhiteSpace(keyword)
                || (m.DisplayNameInTenant != null && m.DisplayNameInTenant.Contains(keyword))
                || u.LoginAccount.Contains(keyword)
                || (u.Phone != null && u.Phone.Contains(keyword))
                || (u.Email != null && u.Email.Contains(keyword))
            orderby m.JoinAt
            select new TenantMemberSummaryRow(
                m.Id,
                m.TenantId,
                u.Id,
                u.LoginAccount,
                m.DisplayNameInTenant ?? u.DisplayName,
                u.Phone,
                u.Email,
                m.OrgId,
                (int)m.Role,
                (int)m.Status,
                m.JoinAt,
                m.LastLoginAt))
            .Skip(skip).Take(take)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<TenantMemberSummaryRow?> GetMemberDetailAsync(
        long tenantMemberId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await (
            from m in db.Set<TenantMember>()
            join u in db.Set<PortalUser>() on m.PortalUserId equals u.Id
            where m.Id == tenantMemberId
            select new TenantMemberSummaryRow(
                m.Id,
                m.TenantId,
                u.Id,
                u.LoginAccount,
                m.DisplayNameInTenant ?? u.DisplayName,
                u.Phone,
                u.Email,
                m.OrgId,
                (int)m.Role,
                (int)m.Status,
                m.JoinAt,
                m.LastLoginAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TenantMemberSummaryRow>> ListAllMembersAsync(
        long tenantId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await (
            from m in db.Set<TenantMember>()
            join u in db.Set<PortalUser>() on m.PortalUserId equals u.Id
            where m.TenantId == tenantId
            select new TenantMemberSummaryRow(
                m.Id,
                m.TenantId,
                u.Id,
                u.LoginAccount,
                m.DisplayNameInTenant ?? u.DisplayName,
                u.Phone,
                u.Email,
                m.OrgId,
                (int)m.Role,
                (int)m.Status,
                m.JoinAt,
                m.LastLoginAt))
            .ToListAsync(cancellationToken);
    }
}
