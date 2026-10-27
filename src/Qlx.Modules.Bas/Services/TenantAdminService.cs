/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21
 * 描述：Bas 模块租户自管理域 Service 实现——原子查询 + 业务校验。
 *       跨库操作（Portal 库 TenantMember/PortalUser/ModuleDefinition）经 Abstractions 契约，禁止 Bas→Portal 直接引用。
 */

using System.Globalization;

using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Messages;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.Numbering;
using Qlx.BuildingBlocks.Validation;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Repositories;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// Bas 租户自管理域 Service。
/// 依赖：ITenantContext（tenantId）+ IBasTenantRepository（租户库）+ Abstractions 跨库契约（Portal 库）。
/// 路由 / 查询 / JSON 属性中的业务 Id 已由收发边界（ModelBinder / JSON 约定）解码为真实 long；
/// 仅请求体顶层数组（无属性元数据）在 Service 内统一解码。
/// </summary>
internal sealed partial class TenantAdminService(
    ITenantContext tenantCtx,
    IBasTenantRepository repo,
    ITenantMemberQuery memberQuery,
    ITenantMemberAdmin memberAdmin,
    IModulePermissionQuery permissionQuery,
    IIdObfuscator obfuscator,
    IMessageService messageService,
    IEncodingGenerator encodingGenerator,
    ICurrentMemberDataScopeResolver dataScopeResolver,
    Services.Numbering.IBasCodeNumberGenerator codeNumberGenerator,
    Qlx.BuildingBlocks.Caching.ICacheService cache) : ITenantAdminService
{
    private long TenantId => tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    // ============ Organization ============

    public async Task<IReadOnlyList<OrganizationFlatRow>> ListAllOrgsAsync(CancellationToken ct)
    {
        IReadOnlyList<Organization> orgs = await repo.ListAllOrgsAsync(ct);

        // X-5 数据范围过滤：组织列表收敛至当前成员可见范围（Self/Dept → 本部门；DeptAndChildren → 子树）
        var scopeCtx = await dataScopeResolver.ResolveAsync(ct);
        if (scopeCtx is not null && scopeCtx.Scope != DataScope.All)
        {
            IReadOnlyCollection<long> allowedOrgIds = scopeCtx.Scope == DataScope.DeptAndChildren
                ? (scopeCtx.OrgId.HasValue ? ExpandSubtreeOrgIds(orgs, scopeCtx.OrgId.Value) : [])
                : (scopeCtx.OrgId.HasValue ? [scopeCtx.OrgId.Value] : []);
            orgs = [.. orgs.Where(o => allowedOrgIds.Contains(o.Id))];
        }

        // 直属成员计数：成员在 Portal 库（跨库禁 Join），一次查询全量成员后内存分组计数（禁 N+1）,
        //企业应用用户不大，暂时不用担心拉爆内存问题，后期量大将分页
        IReadOnlyList<TenantMemberSummaryRow> members = await memberQuery.ListAllMembersAsync(TenantId, ct);
        Dictionary<long, int> memberCountByOrg = members
            .Where(m => m.OrgId.HasValue)
            .GroupBy(m => m.OrgId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        return [.. orgs.Select(o => MapOrg(o, memberCountByOrg.GetValueOrDefault(o.Id)))];
    }

    /// <summary>展开组织子树 Id 集（含自身）：基于已加载的全量组织内存建树，禁 N+1。</summary>
    private static List<long> ExpandSubtreeOrgIds(IReadOnlyList<Organization> allOrgs, long rootOrgId)
    {
        var childrenByParent = allOrgs
            .Where(o => o.ParentId.HasValue)
            .GroupBy(o => o.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(o => o.Id));
        var result = new List<long> { rootOrgId };
        var queue = new Queue<long>([rootOrgId]);
        while (queue.Count > 0)
        {
            if (childrenByParent.TryGetValue(queue.Dequeue(), out var children))
            {
                foreach (var childId in children)
                {
                    result.Add(childId);
                    queue.Enqueue(childId);
                }
            }
        }

        return result;
    }

    public async Task<OrganizationFlatRow> CreateOrgAsync(SaveOrganizationPayload payload, CancellationToken ct)
    {
        // 上级存在性校验
        Organization? parent = null;
        if (payload.ParentId.HasValue)
        {
            parent = await repo.FindOrgAsync(payload.ParentId.Value, ct) ?? throw new NotFoundException("上级组织", PublicId(payload.ParentId.Value));
        }

        OrgNodeType type = ParseOrgType(payload.Type);
        // 组织编码由统一发号器生成（前端不传码，N-1/C-1）
        string code = await codeNumberGenerator.NextAsync(
            "org",
            TenantId,
            parent?.Code,
            Services.Numbering.BasCodeRuleDefaults.GetOrgTypeCode(type),
            isConflict: (candidate, token) => repo.IsOrgCodeExistsAsync(candidate, token),
            ct: ct).ConfigureAwait(false);

        Organization org = new(TenantId, payload.Name.Trim(), type, parent?.Id, payload.SortOrder, code);
        org.SetLeader(payload.LeaderPersonId);
        await repo.AddOrgAsync(org, ct);
        _ = await repo.SaveChangesAsync(ct);
        await InvalidateBasCacheAsync(ct).ConfigureAwait(false);
        return MapOrg(org, 0);
    }

    public async Task<OrganizationFlatRow> UpdateOrgAsync(long id, SaveOrganizationPayload payload, CancellationToken ct)
    {
        Organization org = await repo.FindOrgAsync(id, ct) ?? throw new NotFoundException("组织", PublicId(id));

        // 上级不能是自身

        if (payload.ParentId.HasValue && payload.ParentId.Value == id)
        {
            throw new ConflictException("上级组织不能选择自身", "ORG_PARENT_SELF");
        }

        // 上级不能是自身下级（避免形成环）：一次取全量组织内存展开子树
        if (payload.ParentId.HasValue)
        {
            _ = await repo.FindOrgAsync(payload.ParentId.Value, ct) ?? throw new NotFoundException("上级组织", PublicId(payload.ParentId.Value));
            if (ExpandSubtreeOrgIds(await repo.ListAllOrgsAsync(ct), id).Contains(payload.ParentId.Value))
            {
                throw new ConflictException("上级组织不能选择自身的下级", "ORG_PARENT_CYCLE");
            }
        }

        org.Rename(payload.Name.Trim());
        org.ChangeType(ParseOrgType(payload.Type));
        org.ChangeParent(payload.ParentId);
        org.SetLeader(payload.LeaderPersonId);
        org.SortOrder = payload.SortOrder;
        org.TouchVersion();
        await repo.UpdateOrgAsync(org, ct);
        _ = await repo.SaveChangesAsync(ct);
        await InvalidateBasCacheAsync(ct).ConfigureAwait(false);
        return MapOrg(org, await CountOrgMembersAsync(id, ct));
    }

    public async Task<OrganizationFlatRow> ToggleOrgStatusAsync(long id, CancellationToken ct)
    {
        Organization org = await repo.FindOrgAsync(id, ct) ?? throw new NotFoundException("组织", PublicId(id));

        // 根公司不可停用

        if (org.ParentId is null && org.Status == EntityStatus.Enabled)
        {
            throw new ConflictException("公司根节点不可停用", "ORG_ROOT_PROTECTED");
        }

        if (org.Status == EntityStatus.Enabled)
        {
            org.Disable();
        }
        else
        {
            org.Enable();
        }

        org.TouchVersion();
        await repo.UpdateOrgAsync(org, ct);
        _ = await repo.SaveChangesAsync(ct);
        await InvalidateBasCacheAsync(ct).ConfigureAwait(false);
        return MapOrg(org, await CountOrgMembersAsync(id, ct));
    }

    /// <summary>按租户 Bas 前缀失效缓存（C-5 失效优先：写路径只删不写）。</summary>
    private Task<long> InvalidateBasCacheAsync(CancellationToken ct)
        => cache.RemoveByPrefixAsync(
            $"{Qlx.BuildingBlocks.Caching.QlxCacheKey.TenantKeyPrefix}:{TenantId}:{Qlx.Abstractions.Constants.QlxModuleCodes.Bas}:",
            ct);

    /// <summary>统计单组织直属成员数（低频管理操作专用；列表场景走 ListAllOrgsAsync 的一次性分组计数）。</summary>
    private async Task<int> CountOrgMembersAsync(long orgId, CancellationToken ct)
    {
        IReadOnlyList<TenantMemberSummaryRow> members = await memberQuery.ListAllMembersAsync(TenantId, ct);
        return members.Count(m => m.OrgId == orgId);
    }

    // ============ Position ============

    public async Task<PagedResult<PositionRow>> QueryPositionsAsync(
        long? orgId, string? keyword, PagedRequest request, CancellationToken ct)
    {
        // 选中组织时按「本级 + 全部下级」子树筛选（与页面文案一致）：
        // 组织量小，一次取全量组织后内存展开子树，单条 IN 查询（禁递归 SQL / 禁 N+1）
        IReadOnlyCollection<long>? orgIds = null;
        if (orgId.HasValue)
        {
            IReadOnlyList<Organization> orgs = await repo.ListAllOrgsAsync(ct);
            orgIds = CollectSubtreeOrgIds(orgs, orgId.Value);
        }

        (IReadOnlyList<Position>? items, int total) = await repo.QueryPositionsAsync(
            orgIds, keyword, request.Skip, request.PageSize, ct);
        return new PagedResult<PositionRow>([.. items.Select(MapPosition)], total, request.Page, request.PageSize);
    }

    /// <summary>展开组织子树 Id 集合（含自身；广度优先，防脏数据环形引用导致死循环）。</summary>
    private static List<long> CollectSubtreeOrgIds(IReadOnlyList<Organization> orgs, long rootId)
    {
        Dictionary<long, List<long>> childrenByParent = []
            ;
        foreach (IGrouping<long, Organization> group in orgs.Where(o => o.ParentId.HasValue).GroupBy(o => o.ParentId!.Value))
        {
            childrenByParent[group.Key] = [.. group.Select(o => o.Id)];
        }

        List<long> result = [];
        HashSet<long> visited = [];
        Queue<long> queue = new();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            long current = queue.Dequeue();
            if (!visited.Add(current))
            {
                continue;
            }

            result.Add(current);
            if (childrenByParent.TryGetValue(current, out List<long>? children))
            {
                foreach (long child in children)
                {
                    queue.Enqueue(child);
                }
            }
        }

        return result;
    }

    public async Task<PositionRow> SavePositionAsync(SavePositionPayload payload, CancellationToken ct)
    {
        // 组织存在性
        _ = await repo.FindOrgAsync(payload.OrgId, ct) ?? throw new NotFoundException("所属组织", PublicId(payload.OrgId));

        if (payload.Id.HasValue)
        {
            Position pos = await repo.FindPositionAsync(payload.Id.Value, ct) ?? throw new NotFoundException("岗位", PublicId(payload.Id.Value));

            // 岗位编码由系统生成，不允许修改
            pos.Rename(payload.Name.Trim());
            pos.ChangeOrg(payload.OrgId);
            pos.SetHeadcount(payload.Headcount);
            pos.SetDescriptions(payload.Description, payload.Duty);
            await repo.UpdatePositionAsync(pos, ct);
            _ = await repo.SaveChangesAsync(ct);
            await InvalidateBasCacheAsync(ct).ConfigureAwait(false);
            return MapPosition(pos);
        }
        else
        {
            // 岗位编码由系统自动生成：pos- 前缀 + 岗位名拼音首字母，冲突追加 001/002...
            string code = await encodingGenerator.GenerateAsync(
                payload.Name,
                scope: "position",
                uniquenessChecker: async (c, ct2) => await repo.IsPositionCodeExistsAsync(c, null, ct2).ConfigureAwait(false),
                options: new EncodingGenerationOptions
                {
                    Prefix = "pos-",
                    PinyinMode = PinyinMode.Initials,
                    SequenceDigits = 3,
                    MaxLength = 50,
                },
                cancellationToken: ct).ConfigureAwait(false);

            Position pos = new(TenantId, payload.OrgId, payload.Name.Trim(), code, payload.Headcount);
            pos.SetDescriptions(payload.Description, payload.Duty);
            await repo.AddPositionAsync(pos, ct);
            _ = await repo.SaveChangesAsync(ct);
            await InvalidateBasCacheAsync(ct).ConfigureAwait(false);
            return MapPosition(pos);
        }
    }

    public async Task<PositionRow> TogglePositionStatusAsync(long id, CancellationToken ct)
    {
        Position pos = await repo.FindPositionAsync(id, ct) ?? throw new NotFoundException("岗位", PublicId(id));

        // 状态机翻转；有在职成员不阻断（与组织停用语义一致，由前端提示）
        if (pos.Status == EntityStatus.Enabled)
        {
            pos.Disable();
        }
        else
        {
            pos.Enable();
        }

        await repo.UpdatePositionAsync(pos, ct);
        _ = await repo.SaveChangesAsync(ct);
        await InvalidateBasCacheAsync(ct).ConfigureAwait(false);
        return MapPosition(pos);
    }

    // ============ Member（跨 Portal 库）============

    public async Task<PagedResult<MemberRow>> QueryMembersAsync(
        long? orgId, int? memberRole, int? memberStatus, string? keyword,
        PagedRequest request, CancellationToken ct)
    {
        // X-5 数据范围过滤：解析当前成员范围上下文（TenantAdmin/无成员上下文 → null 不过滤）
        var scopeCtx = await dataScopeResolver.ResolveAsync(ct);
        IReadOnlyCollection<long>? scopeOrgIds = null;
        long? onlyMemberId = null;
        if (scopeCtx is not null && scopeCtx.Scope != DataScope.All)
        {
            if (scopeCtx.Scope == DataScope.Self)
            {
                onlyMemberId = scopeCtx.MemberId;
            }
            else
            {
                // Dept / DeptAndChildren（Custom 已在解析器降级为 Dept）
                if (!scopeCtx.OrgId.HasValue)
                {
                    // 成员未挂部门：范围内无任何可见成员
                    return new PagedResult<MemberRow>([], 0, request.Page, request.PageSize);
                }

                scopeOrgIds = scopeCtx.Scope == DataScope.DeptAndChildren
                    ? ExpandSubtreeOrgIds(await repo.ListAllOrgsAsync(ct), scopeCtx.OrgId.Value)
                    : [scopeCtx.OrgId.Value];
            }
        }

        // 显式 orgId 过滤与数据范围求交：越范围直接空页
        if (orgId.HasValue && scopeOrgIds is not null)
        {
            if (!scopeOrgIds.Contains(orgId.Value))
            {
                return new PagedResult<MemberRow>([], 0, request.Page, request.PageSize);
            }

            scopeOrgIds = [orgId.Value];
        }

        (IReadOnlyList<TenantMemberSummaryRow>? items, int total) = await memberQuery.QueryMembersAsync(
            TenantId, keyword, orgId, memberRole, memberStatus,
            request.Skip, request.PageSize, ct, scopeOrgIds, onlyMemberId);

        // 查角色编码列表（按 TenantMemberId 批量查 member_roles + role.code）
        // 为减少查询量，批量查出本租户全部 member_roles + roles，内存分组
        IReadOnlyList<(long TenantMemberId, long RoleId, string RoleCode)> roleLinks = await repo.ListAllMemberRoleCodesFlatAsync(TenantId, ct);
        Dictionary<long, List<string>> roleCodesByMember = roleLinks
            .GroupBy(x => x.TenantMemberId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.RoleCode).ToList());

        List<MemberRow> rows = [.. items.Select(m => new MemberRow(
            m.TenantMemberId,
            m.PortalUserId,
            m.LoginAccount,
            m.DisplayName,
            m.Phone,
            m.Email,
            m.OrgId,
            roleCodesByMember.TryGetValue(m.TenantMemberId, out List<string>? codes) ? codes : [],
            m.MemberRole,
            m.MemberStatus,
            m.JoinAt,
            m.LastLoginAt))];

        return new PagedResult<MemberRow>(rows, total, request.Page, request.PageSize);
    }

    public async Task<MemberRow> GetMemberDetailAsync(long tenantMemberId, CancellationToken ct)
    {
        TenantMemberSummaryRow m = await GetTenantMemberOr404Async(tenantMemberId, ct);

        // X-5 数据范围校验：范围外成员按不存在处理（防 IDOR 探测）
        var scopeCtx = await dataScopeResolver.ResolveAsync(ct);
        if (scopeCtx is not null && scopeCtx.Scope != DataScope.All && tenantMemberId != scopeCtx.MemberId)
        {
            var visible = scopeCtx.Scope switch
            {
                DataScope.DeptAndChildren => m.OrgId.HasValue && scopeCtx.OrgId.HasValue
                    && ExpandSubtreeOrgIds(await repo.ListAllOrgsAsync(ct), scopeCtx.OrgId.Value).Contains(m.OrgId.Value),
                DataScope.Dept => scopeCtx.OrgId.HasValue && m.OrgId == scopeCtx.OrgId,
                _ => false, // Self：非本人不可见
            };
            if (!visible)
            {
                throw new NotFoundException("成员", PublicId(tenantMemberId));
            }
        }

        IReadOnlyList<string> roleCodes = await repo.ListMemberRoleCodesAsync(tenantMemberId, ct);
        return new MemberRow(
            m.TenantMemberId, m.PortalUserId, m.LoginAccount, m.DisplayName,
            m.Phone, m.Email, m.OrgId, [.. roleCodes],
            m.MemberRole, m.MemberStatus, m.JoinAt, m.LastLoginAt);
    }

    public async Task<(long TenantMemberId, long PortalUserId)?> InviteMemberAsync(
        InviteMemberPayload payload, CancellationToken ct)
    {
        // 邮箱/手机格式
        string account = payload.Account.Trim();
        if (!IsValidAccount(account))
        {
            throw new ValidationException("account", "请输入有效的手机号或邮箱");
        }

        // 所属组织存在
        Organization org = await repo.FindOrgAsync(payload.OrgId, ct) ?? throw new NotFoundException("所属组织", PublicId(payload.OrgId));

        // 角色编码存在

        if (payload.RoleCodes.Count > 0)
        {
            HashSet<string> validCodes = [.. (await repo.ListRolesAsync(ct)).Select(r => r.Code)];
            foreach (string code in payload.RoleCodes)
            {
                if (!validCodes.Contains(code))
                {
                    throw new ValidationException("roleCodes", $"角色「{code}」不存在");
                }
            }
        }

        (long TenantMemberId, long PortalUserId) result = await memberAdmin.InviteMemberAsync(
            TenantId, account, payload.DisplayName.Trim(), payload.OrgId, ct) ?? throw new ConflictException("该账号已是本企业成员，请勿重复邀请", "MEMBER_ALREADY_EXISTS");

        // 写入 member_roles（Bas 租户库）

        if (payload.RoleCodes.Count > 0)
        {
            List<(long Id, string Code)> roleIds = [.. (await repo.ListRolesAsync(ct))
                .Where(r => payload.RoleCodes.Contains(r.Code))
                .Select(r => (r.Id, r.Code))];

            List<(long Id, string Code)> roles = [.. roleIds.Select(r => (r.Id, r.Code))];
            await repo.ReplaceMemberRolesAsync(result.TenantMemberId, TenantId, roles, ct);
            _ = await repo.SaveChangesAsync(ct);
        }

        return result;
    }

    public async Task<ImportMembersResult> ImportMembersAsync(
        IReadOnlyList<ImportMemberRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return new ImportMembersResult(0, []);
        }

        // 预载本租户组织与角色一次，内存按名称匹配（禁逐行查库）；每行独立提交，行级失败互不影响
        Dictionary<string, Organization> orgByName = (await repo.ListAllOrgsAsync(ct))
            .GroupBy(o => o.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        Dictionary<string, Role> roleByName = (await repo.ListRolesAsync(ct))
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        int invited = 0;
        List<(int Line, string Reason)> failed = [];

        foreach (ImportMemberRow row in rows)
        {
            string account = row.Account.Trim();
            string displayName = row.DisplayName.Trim();

            if (!IsValidAccount(account))
            {
                failed.Add((row.Line, "账号格式无效（需手机号或邮箱）"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                failed.Add((row.Line, "姓名不能为空"));
                continue;
            }

            if (!orgByName.TryGetValue(row.OrgName.Trim(), out Organization? org))
            {
                failed.Add((row.Line, $"组织「{row.OrgName}」不存在"));
                continue;
            }

            Role? role = null;
            string roleName = row.RoleName.Trim();
            if (roleName.Length > 0 && !roleByName.TryGetValue(roleName, out role))
            {
                failed.Add((row.Line, $"角色「{row.RoleName}」不存在"));
                continue;
            }

            // 邀请（Portal 库独立上下文提交，天然行级隔离）；已是成员返回 null
            (long TenantMemberId, long PortalUserId)? result = await memberAdmin.InviteMemberAsync(
                TenantId, account, displayName, org.Id, ct);
            if (result is not { } invitedIds)
            {
                failed.Add((row.Line, "该账号已是本企业成员"));
                continue;
            }

            if (role is not null)
            {
                await repo.ReplaceMemberRolesAsync(invitedIds.TenantMemberId, TenantId, [(role.Id, role.Code)], ct);
                _ = await repo.SaveChangesAsync(ct);
            }

            invited++;
        }

        return new ImportMembersResult(invited, failed);
    }

    public async Task ResendMemberInviteAsync(long tenantMemberId, CancellationToken ct)
    {
        TenantMemberSummaryRow m = await GetTenantMemberOr404Async(tenantMemberId, ct);

        // Portal 侧权威校验（仅 PendingInvite，409）并返回关联自然人 Id
        long portalUserId = await memberAdmin.ResendInviteAsync(m.TenantMemberId, ct);

        // 经既有 IMessageService 直推站内通知（不新增通道；收件人 Id 与 sub claim 同为混淆串）
        await messageService.PushAsync(new PushRequest(
            obfuscator.Encode(portalUserId),
            "企业邀请",
            $"「{m.DisplayName}」，诚邀您加入企业，请登录后进入租户完成激活。",
            MessageChannel.InApp,
            MessagePriority.Normal,
            new MessageBizRef("TenantMemberInvite", portalUserId.ToString(CultureInfo.InvariantCulture))), ct);
    }

    public async Task<PagedResult<MemberLoginLogRow>> GetMemberLoginLogsAsync(
        long tenantMemberId, PagedRequest request, CancellationToken ct)
    {
        TenantMemberSummaryRow m = await GetTenantMemberOr404Async(tenantMemberId, ct);

        // 页大小上限 20（规范 5.2 登录日志条款）
        int pageSize = Math.Min(request.SafePageSize, 20);
        int skip = Math.Max(request.Page - 1, 0) * pageSize;
        (IReadOnlyList<Qlx.BuildingBlocks.Support.Logs.TenantLogLoginEntity> items, int total) =
            await repo.QueryLoginLogsAsync(m.LoginAccount, skip, pageSize, ct);

        List<MemberLoginLogRow> rows = [.. items.Select(l => new MemberLoginLogRow(
            l.EventId, l.LoginAt, l.IpAddress, l.IsSuccess, l.FailureReason))];
        return new PagedResult<MemberLoginLogRow>(rows, total, request.Page, pageSize);
    }

    /// <summary>取成员摘要并做租户归属校验（越租户一律 404，防越权探测）。</summary>
    private async Task<TenantMemberSummaryRow> GetTenantMemberOr404Async(long tenantMemberId, CancellationToken ct)
    {
        TenantMemberSummaryRow m = await memberQuery.GetMemberDetailAsync(tenantMemberId, ct)
            ?? throw new NotFoundException("成员", PublicId(tenantMemberId));
        return m.TenantId != TenantId
            ? throw new NotFoundException("成员", PublicId(tenantMemberId))
            : m;
    }

    public async Task ToggleMemberStatusAsync(long tenantMemberId, CancellationToken ct)
    {
        await memberAdmin.ToggleMemberStatusAsync(tenantMemberId, ct);
    }

    public async Task ResetMemberPasswordAsync(long tenantMemberId, string newPassword, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            throw new ValidationException("newPassword", "新密码不能为空");
        }

        await memberAdmin.ResetMemberPasswordAsync(tenantMemberId, newPassword, ct);
    }

    public async Task RemoveMemberAsync(long tenantMemberId, CancellationToken ct)
    {
        // 先清 member_roles（Bas 库）再删 TenantMember（Portal 库）
        await repo.RemoveMemberRoleLinksAsync(tenantMemberId, ct);
        _ = await repo.SaveChangesAsync(ct);
        await memberAdmin.RemoveMemberFromTenantAsync(tenantMemberId, ct);
    }

    // ============ Role ============

    public async Task<IReadOnlyList<RoleRow>> ListRolesAsync(CancellationToken ct)
    {
        IReadOnlyList<(Role Role, int MemberCount, int PermissionCount)> roles = await repo.ListRolesWithCountsAsync(ct);
        return [.. roles.Select(x => MapRole(x.Role, x.MemberCount, x.PermissionCount))];
    }

    public async Task<RoleRow> CreateRoleAsync(SaveRolePayload payload, CancellationToken ct)
    {
        // 角色编码由系统自动生成：role- 前缀 + 角色名拼音首字母，冲突追加 001/002...
        string code = await encodingGenerator.GenerateAsync(
            payload.Name,
            scope: "role",
            uniquenessChecker: async (c, ct2) => await repo.IsRoleCodeExistsAsync(c, null, ct2).ConfigureAwait(false),
            options: new EncodingGenerationOptions
            {
                Prefix = "role-",
                PinyinMode = PinyinMode.Initials,
                SequenceDigits = 3,
                MaxLength = 50,
            },
            cancellationToken: ct).ConfigureAwait(false);

        Role role = new(TenantId, code, payload.Name)
        {
            Description = payload.Description,
            DefaultDataScope = (DataScope)payload.DataScope,
        };
        await repo.AddRoleAsync(role, ct);
        _ = await repo.SaveChangesAsync(ct);
        return MapRole(role, 0, 0);
    }

    public async Task<RoleRow> UpdateRoleAsync(long id, SaveRolePayload payload, CancellationToken ct)
    {
        Role role = await repo.FindRoleAsync(id, ct) ?? throw new NotFoundException("角色", PublicId(id));

        if (role.IsBuiltin)
        {
            throw new ForbiddenException("内置角色不可修改");
        }

        // 角色编码由系统生成，不允许修改（唯一索引 + 引用完整性）
        role.Name = payload.Name;
        role.Description = payload.Description;
        role.DefaultDataScope = (DataScope)payload.DataScope;
        await repo.UpdateRoleAsync(role, ct);
        _ = await repo.SaveChangesAsync(ct);

        (int memberCount, int permissionCount) = await repo.GetRoleCountsAsync(role.Id, ct);
        return MapRole(role, memberCount, permissionCount);
    }

    public async Task DeleteRoleAsync(long id, CancellationToken ct)
    {
        Role role = await repo.FindRoleAsync(id, ct) ?? throw new NotFoundException("角色", PublicId(id));

        if (role.IsBuiltin)
        {
            throw new ForbiddenException("内置角色不可删除");
        }

        // 清理 role_permissions + member_roles 关联
        await repo.ReplaceRolePermissionsAsync(role.Id, TenantId, [], ct);
        await repo.RemoveMemberRoleLinksByRoleAsync(role.Id, ct);
        await repo.RemoveRoleAsync(role, ct);
        _ = await repo.SaveChangesAsync(ct);
    }

    public async Task SaveRolePermissionsAsync(long id, IReadOnlyList<string> permissionCodes, CancellationToken ct)
    {
        Role role = await repo.FindRoleAsync(id, ct) ?? throw new NotFoundException("角色", PublicId(id));

        if (role.IsBuiltin)
        {
            throw new ForbiddenException("内置角色的功能权限由系统固定，不可调整");
        }

        // 过滤到合法权限码（可选项：与 Portal 库 PermissionDefinitions 做交集校验）
        await repo.ReplaceRolePermissionsAsync(role.Id, TenantId, permissionCodes, ct);
        _ = await repo.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetRolePermissionCodesAsync(long id, CancellationToken ct)
    {
        Role role = await repo.FindRoleAsync(id, ct) ?? throw new NotFoundException("角色", PublicId(id));
        return await repo.ListRolePermissionCodesAsync(role.Id, ct);
    }

    public async Task<IReadOnlyList<string>> GetRoleMemberIdsAsync(long id, CancellationToken ct)
    {
        Role role = await repo.FindRoleAsync(id, ct) ?? throw new NotFoundException("角色", PublicId(id));

        // 顶层 long 数组无属性元数据可挂混淆约定，与 PUT roles/{id}/members 入参对称：Service 统一编码为对外混淆串
        IReadOnlyList<long> memberIds = await repo.ListRoleMemberIdsAsync(role.Id, ct);
        return [.. memberIds.Select(obfuscator.Encode)];
    }

    public async Task SaveRoleMembersAsync(long id, IReadOnlyList<string> tenantMemberIds, CancellationToken ct)
    {
        Role role = await repo.FindRoleAsync(id, ct) ?? throw new NotFoundException("角色", PublicId(id));

        // 顶层 JSON 数组无属性元数据，无法由全局 Id 约定处理，统一在 Service 解码

        List<long> memberIds = [.. tenantMemberIds.Select(memberId => DecodeId(memberId, "成员"))];

        // 替换本角色的全部成员关联（先清后写）
        await repo.ReplaceRoleMemberLinksAsync(role.Id, memberIds, TenantId, ct);
        _ = await repo.SaveChangesAsync(ct);
    }

    // ============ 只读（来自 Portal 库）============

    public async Task<IReadOnlyList<PermissionGroupRow>> ListPermissionGroupsAsync(CancellationToken ct)
    {
        IReadOnlyList<ModuleDefinitionRow> modules = await permissionQuery.ListAllModulesWithPermissionsAsync(ct);
        return [.. modules.Select(m => new PermissionGroupRow(
            m.Code, m.Name, m.Icon,
            [.. m.Permissions.Select(p => new PermissionPointRow(p.Code, p.Name, p.Description))]))];
    }

    public async Task<IReadOnlyList<TenantModuleRow>> ListTenantModulesAsync(CancellationToken ct)
    {
        IReadOnlyList<TenantModuleCardRow> cards = await permissionQuery.ListTenantModulesAsync(TenantId, ct);
        return [.. cards.Select(c => new TenantModuleRow(
            c.Code, c.Name, c.Description, c.Icon, c.Version, c.Enabled, c.LicensedAt))];
    }

    // ============ 私有辅助 ============

    /// <summary>把真实 Id 还原为对外混淆串（仅用于异常消息，避免向调用方泄露内部编号）。</summary>
    private string PublicId(long id)
    {
        return obfuscator.Encode(id);
    }

    /// <summary>解码对外混淆 Id（仅用于无属性元数据的请求体顶层数组）；非法或越界一律视为资源不存在。</summary>
    private long DecodeId(string? publicId, string resourceType)
    {
        return !obfuscator.TryDecode(publicId, out long id) || id <= 0
            ? throw new NotFoundException(resourceType, publicId ?? string.Empty)
            : id;
    }

    private static OrgNodeType ParseOrgType(string type)
    {
        // 前端旧值 / 数值枚举双通道：Company→Group、Team→Department（D2 类型重定义后的兼容映射）
        if (int.TryParse(type, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numeric)
            && Enum.IsDefined(typeof(OrgNodeType), numeric))
        {
            return (OrgNodeType)numeric;
        }

        return type switch
        {
            "Group" or "Company" => OrgNodeType.Group,
            "Subsidiary" => OrgNodeType.Subsidiary,
            "Department" or "Team" => OrgNodeType.Department,
            "Project" => OrgNodeType.Project,
            _ => throw new ValidationException("type", $"无效的节点类型：{type}"),
        };
    }

    private static bool IsValidAccount(string a)
    {
        // 通用格式正则统一来自公共层（预编译），本模块禁止重复定义
        return CommonValidationPatterns.IsCnMobilePhone(a)
            || CommonValidationPatterns.IsEmail(a);
    }

    private static OrganizationFlatRow MapOrg(Organization o, int memberCount)
    {
        return new(
        o.Id, o.ParentId, o.Code, o.Name, o.Type.ToString(),
        o.LeaderPersonId, o.Status.ToString(), o.SortOrder, o.IsBuiltin, o.CreatedAt, memberCount);
    }

    private static RoleRow MapRole(Role r, int memberCount, int permissionCount)
    {
        return new(
            r.Id, r.Code, r.Name, r.Description, r.IsBuiltin,
            (int)r.DefaultDataScope, memberCount, permissionCount, r.UpdatedAt);
    }

    private static PositionRow MapPosition(Position p)
    {
        return new(
        p.Id, p.OrgId, p.Name, p.Code, p.Headcount, p.CurrentCount,
            p.Status.ToString(), p.Description, p.Duty, p.UpdatedAt);
    }
}
