/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 18:10:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 18:10:00 +08:00 中国标准时间 星期三
 * 描述：门户管理端仓储契约：租户 / 用户 / 模块三条聚合读路径（分层样板：Controller→Service→Repository）
 */

using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Portal.Domain.Identity;
using Qlx.Modules.Portal.Domain.Tenancy;

namespace Qlx.Modules.Portal.Repositories;

/// <summary>租户成员投影行（详情页成员只读展示，避免把 PortalUser 整体带出）。</summary>
public sealed record TenantMemberRow(
    long Id,
    long PortalUserId,
    string LoginAccount,
    string DisplayName,
    string? DisplayNameInTenant,
    TenantMemberRole Role,
    MemberStatus Status,
    DateTimeOffset? JoinAt,
    DateTimeOffset? LastLoginAt);

/// <summary>用户详情页成员关系投影行（带租户名称与编码）。</summary>
public sealed record UserMembershipRow(
    long Id,
    long PortalUserId,
    string LoginAccount,
    long TenantId,
    string TenantName,
    string TenantCode,
    string? DisplayNameInTenant,
    TenantMemberRole Role,
    MemberStatus Status,
    DateTimeOffset? JoinAt,
    DateTimeOffset? LastLoginAt);

/// <summary>模块定义投影行（供租户授权校验与授权记录名称快照，避免服务层依赖域实体构造）。</summary>
public sealed record ModuleDefinitionRow(
    string ModuleCode,
    string DisplayName,
    string CurrentVersion,
    bool Licensable,
    bool IsMandatory);

/// <summary>模块目录投影行（模块定义 + 授权租户数 + 菜单功能点）。</summary>
public sealed record ModuleCatalogRow(
    string ModuleCode,
    string DisplayName,
    string CurrentVersion,
    string? Description,
    int LicensedTenantCount,
    IReadOnlyList<string> Features);

/// <summary>租户管理仓储。</summary>
public interface IPortalTenantAdminRepository
{
    /// <summary>分页查询租户（关键字匹配名称/编码/税号；status 为前端状态字面量过滤；databaseMode Shared/Isolated 可选）。</summary>
    Task<PagedResult<Tenant>> QueryAsync(
        string? keyword, string? statusFilter, string? databaseModeFilter, PagedRequest request, CancellationToken cancellationToken);

    /// <summary>按 Id 查询租户（跟踪态，用于状态变更）。</summary>
    Task<Tenant?> FindTrackedAsync(long tenantId, CancellationToken cancellationToken);

    /// <summary>按 Id 查询租户（只读，含数据库连接与模块许可）。</summary>
    Task<Tenant?> FindWithDetailsAsync(long tenantId, CancellationToken cancellationToken);

    /// <summary>按 Id 查询租户（跟踪态，含模块许可，用于授权变更）。</summary>
    Task<Tenant?> FindTrackedWithModulesAsync(long tenantId, CancellationToken cancellationToken);

    /// <summary>租户编码是否已存在。</summary>
    Task<bool> CodeExistsAsync(string tenantCode, CancellationToken cancellationToken);

    /// <summary>统一社会信用代码是否已存在。</summary>
    Task<bool> TaxNoExistsAsync(string taxNo, CancellationToken cancellationToken);

    /// <summary>批量取租户最近登录时间（租户成员最近进入时间的最大值）。</summary>
    Task<IReadOnlyDictionary<long, DateTimeOffset?>> GetLastLoginTimesAsync(
        IReadOnlyCollection<long> tenantIds, CancellationToken cancellationToken);

    /// <summary>查询租户许可变更记录（tenantId 为空时查全部门户级记录）。</summary>
    Task<IReadOnlyList<LicenseRecord>> QueryLicenseRecordsAsync(
        long? tenantId, int take, CancellationToken cancellationToken);

    /// <summary>查询租户成员关系投影。</summary>
    Task<IReadOnlyList<TenantMemberRow>> ListMembersAsync(long tenantId, CancellationToken cancellationToken);

    /// <summary>新增租户（连同数据库登记与模块许可一次性提交）。</summary>
    Task AddAsync(Tenant tenant, CancellationToken cancellationToken);

    /// <summary>登记租户数据库连接信息。</summary>
    Task AddDatabaseAsync(TenantDatabase database, CancellationToken cancellationToken);

    /// <summary>新增租户模块许可。</summary>
    Task AddModulesAsync(IReadOnlyCollection<TenantModule> modules, CancellationToken cancellationToken);

    /// <summary>新增许可变更记录。</summary>
    Task AddLicenseRecordsAsync(IReadOnlyCollection<LicenseRecord> records, CancellationToken cancellationToken);

    /// <summary>新增租户成员关系。</summary>
    Task AddMemberAsync(TenantMember member, CancellationToken cancellationToken);

    /// <summary>提交变更。</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>门户用户管理仓储。</summary>
public interface IPortalUserAdminRepository
{
    /// <summary>分页查询门户用户。</summary>
    Task<PagedResult<PortalUser>> QueryAsync(
        string? keyword, string? statusFilter, PagedRequest request, CancellationToken cancellationToken);

    /// <summary>按 Id 查询用户（跟踪态，用于启停 / 重置密码）。</summary>
    Task<PortalUser?> FindTrackedAsync(long userId, CancellationToken cancellationToken);

    /// <summary>按登录账号查询用户（跟踪态，新建租户时匹配或创建管理员）。</summary>
    Task<PortalUser?> FindTrackedByAccountAsync(string loginAccount, CancellationToken cancellationToken);

    /// <summary>查询用户的租户成员关系投影。</summary>
    Task<IReadOnlyList<UserMembershipRow>> ListMembershipsAsync(long userId, CancellationToken cancellationToken);

    /// <summary>查询用户最近 N 条密码历史（按 CreatedAt 降序）。</summary>
    Task<IReadOnlyList<PortalUserPasswordHistory>> GetRecentPasswordHistoriesAsync(
        long userId, int take, CancellationToken cancellationToken);

    /// <summary>新增一条密码历史记录。</summary>
    Task AddPasswordHistoryAsync(PortalUserPasswordHistory history, CancellationToken cancellationToken);

    /// <summary>清理超过保留上限的最旧密码历史记录。</summary>
    Task TrimPasswordHistoriesAsync(long userId, int keepLatest, CancellationToken cancellationToken);

    /// <summary>新增门户用户。</summary>
    Task AddAsync(PortalUser user, CancellationToken cancellationToken);

    /// <summary>提交变更。</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>模块目录与许可管理仓储。</summary>
public interface IPortalModuleAdminRepository
{
    /// <summary>查询模块目录（模块定义 + 已授权租户数 + 功能点）。</summary>
    Task<IReadOnlyList<ModuleCatalogRow>> ListCatalogAsync(CancellationToken cancellationToken);

    /// <summary>查询全部模块定义（校验必备模块与可授权性）。</summary>
    Task<IReadOnlyList<ModuleDefinitionRow>> ListDefinitionsAsync(CancellationToken cancellationToken);

    /// <summary>查询指定编码的模块定义（用于校验可授权性）。</summary>
    Task<IReadOnlyList<ModuleDefinition>> ListByCodesAsync(
        IReadOnlyCollection<string> moduleCodes, CancellationToken cancellationToken);

    /// <summary>查询门户工作台聚合指标（租户总数 / 启用租户 / 门户用户 / 模块平均授权数）。</summary>
    Task<PortalWorkbenchCounters> GetWorkbenchCountersAsync(CancellationToken cancellationToken);
}

/// <summary>门户工作台聚合计数。</summary>
public sealed record PortalWorkbenchCounters(
    long TenantTotal,
    long TenantEnabled,
    long TenantPending,
    long TenantProvisioningFailed,
    long UserTotal,
    long UserDisabled,
    long LicensedTenantModuleCount);
