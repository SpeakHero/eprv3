/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 18:40:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 22:35:00 +08:00 中国标准时间 星期三
 * 描述：门户管理端实体 → DTO 映射（枚举字面量与前端契约严格一致，禁止在页面散落映射）
 */

using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Portal.Domain.Identity;
using Qlx.Modules.Portal.Domain.Tenancy;
using Qlx.Modules.Portal.Dtos;
using Qlx.Modules.Portal.Repositories;

namespace Qlx.Modules.Portal.Services;

/// <summary>门户管理端映射器。</summary>
internal static class PortalAdminMapper
{
    /// <summary>租户状态 → 前端字面量。</summary>
    public static string ToTenantStatusText(TenantStatus status) => status switch
    {
        TenantStatus.Active => PortalConstants.Frontend.Enabled,
        TenantStatus.Provisioning => PortalConstants.Frontend.Pending,
        TenantStatus.GracePeriod => PortalConstants.Frontend.Expired,
        TenantStatus.ProvisioningFailed => PortalConstants.Frontend.ProvisioningFailed,
        _ => PortalConstants.Frontend.Disabled,
    };

    /// <summary>数据库状态 → 前端字面量。</summary>
    public static string ToDatabaseStatusText(Tenant tenant) => tenant.Status switch
    {
        TenantStatus.Provisioning => PortalConstants.Frontend.Provisioning,
        TenantStatus.ProvisioningFailed => PortalConstants.Frontend.Failed,
        _ when tenant.Database is null => PortalConstants.Frontend.Failed,
        _ => tenant.Database.Status switch
        {
            TenantDatabaseStatus.Migrating => PortalConstants.Frontend.Provisioning,
            TenantDatabaseStatus.Archived => PortalConstants.Frontend.Ready,
            _ => PortalConstants.Frontend.Ready,
        },
    };

    /// <summary>套餐标识 → 会员等级字面量。</summary>
    public static string ToMemberLevelText(string? planCode) => planCode?.ToLowerInvariant() switch
    {
        PortalConstants.PlanCodes.Trial => PortalConstants.MemberLevels.Trial,
        PortalConstants.PlanCodes.Professional => PortalConstants.MemberLevels.Professional,
        PortalConstants.PlanCodes.Flagship => PortalConstants.MemberLevels.Flagship,
        _ => PortalConstants.MemberLevels.Standard,
    };

    /// <summary>会员等级字面量 → 套餐标识（存库小写）。</summary>
    public static string ToPlanCode(string memberLevel) => memberLevel switch
    {
        PortalConstants.MemberLevels.Trial => PortalConstants.PlanCodes.Trial,
        PortalConstants.MemberLevels.Professional => PortalConstants.PlanCodes.Professional,
        PortalConstants.MemberLevels.Flagship => PortalConstants.PlanCodes.Flagship,
        _ => PortalConstants.PlanCodes.Standard,
    };

    /// <summary>数据库模式 → 前端字面量。</summary>
    public static string ToDatabaseModeText(TenantDatabaseMode mode)
        => mode == TenantDatabaseMode.Shared ? PortalConstants.Frontend.Shared : PortalConstants.Frontend.Isolated;

    /// <summary>租户实体 → 列表项。</summary>
    public static PortalTenantItem ToTenantItem(
        Tenant tenant, IIdObfuscator obfuscator, DateTimeOffset? lastLoginAt)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(obfuscator);

        return new PortalTenantItem
        {
            Id = obfuscator.Encode(tenant.Id),
            Name = tenant.DisplayName,
            Code = tenant.TenantCode,
            TaxNo = tenant.TaxNo ?? string.Empty,
            Status = ToTenantStatusText(tenant.Status),
            MemberLevel = ToMemberLevelText(tenant.PlanCode),
            MemberExpireAt = tenant.SubscriptionExpiresAt,
            DbStatus = ToDatabaseStatusText(tenant),
            DatabaseMode = ToDatabaseModeText(tenant.Database?.DatabaseMode ?? tenant.DatabaseMode),
            DbName = tenant.Database?.DbName,
            DbError = tenant.LastErrorMessage,
            LicensedModuleCodes = tenant.Modules
                .Where(m => m.Status == TenantModuleStatus.Enabled)
                .Select(m => m.ModuleCode)
                .OrderBy(c => c, StringComparer.Ordinal)
                .ToList(),
            CreatedAt = tenant.CreatedAt,
            LastLoginAt = lastLoginAt,
        };
    }

    /// <summary>租户数据库导航 → 详情摘要（凭据不返回）。</summary>
    public static PortalTenantDatabaseSummary? ToDatabaseSummary(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        if (tenant.Database is null)
        {
            return null;
        }

        var database = tenant.Database;
        return new PortalTenantDatabaseSummary
        {
            Mode = ToDatabaseModeText(database.DatabaseMode),
            DbName = database.DbName,
            DbHost = database.DbHost,
            DbPort = database.DbPort,
            Status = database.Status switch
            {
                TenantDatabaseStatus.Migrating => PortalConstants.Frontend.Provisioning,
                TenantDatabaseStatus.Archived => PortalConstants.Frontend.Archived,
                _ => PortalConstants.Frontend.Ready,
            },
            PoolCode = database.PoolCode,
        };
    }

    /// <summary>许可记录 → DTO。</summary>
    public static LicenseRecordItem ToLicenseRecordItem(LicenseRecord record, IIdObfuscator obfuscator) => new()
    {
        Id = obfuscator.Encode(record.Id),
        TenantId = obfuscator.Encode(record.TenantId),
        TenantName = record.TenantName,
        ModuleCode = record.ModuleCode,
        ModuleName = record.ModuleName,
        Action = record.Action switch
        {
            LicenseAction.Granted => PortalConstants.Frontend.Granted,
            LicenseAction.Revoked => PortalConstants.Frontend.Revoked,
            _ => PortalConstants.Frontend.Adjusted,
        },
        OperatorName = record.OperatorName,
        At = record.At,
        Remark = record.Remark,
    };

    /// <summary>门户用户 → DTO。</summary>
    public static PortalUserItem ToUserItem(PortalUser user, IIdObfuscator obfuscator) => new()
    {
        Id = obfuscator.Encode(user.Id),
        LoginAccount = user.LoginAccount,
        DisplayName = user.DisplayName,
        Phone = user.Phone ?? string.Empty,
        Email = user.Email ?? string.Empty,
        Status = user.Status == UserStatus.Enabled ? PortalConstants.Frontend.Enabled : PortalConstants.Frontend.Disabled,
        RealNameVerified = user.RealNameVerified,
        IsPortalAdmin = user.IsPortalAdmin,
        LastLoginAt = user.LastLoginAt,
        CreatedAt = user.CreatedAt,
    };

    /// <summary>租户成员投影 → 成员关系 DTO（租户详情页，租户信息由调用方提供）。</summary>
    public static TenantMembershipItem ToMembershipItem(
        TenantMemberRow row, Tenant tenant, IIdObfuscator obfuscator) => new()
    {
        Id = obfuscator.Encode(row.Id),
        PortalUserId = obfuscator.Encode(row.PortalUserId),
        LoginAccount = row.LoginAccount,
        TenantId = obfuscator.Encode(tenant.Id),
        TenantName = tenant.DisplayName,
        TenantCode = tenant.TenantCode,
        DisplayNameInTenant = row.DisplayNameInTenant ?? row.DisplayName,
        MemberRole = ToMemberRoleText(row.Role),
        MemberStatus = row.Status == MemberStatus.Enabled ? PortalConstants.Frontend.Enabled : PortalConstants.Frontend.Disabled,
        JoinAt = row.JoinAt ?? tenant.CreatedAt,
        LastLoginAt = row.LastLoginAt,
    };

    /// <summary>用户详情成员关系投影 → DTO。</summary>
    public static TenantMembershipItem ToMembershipItem(UserMembershipRow row, IIdObfuscator obfuscator) => new()
    {
        Id = obfuscator.Encode(row.Id),
        PortalUserId = obfuscator.Encode(row.PortalUserId),
        LoginAccount = row.LoginAccount,
        TenantId = obfuscator.Encode(row.TenantId),
        TenantName = row.TenantName,
        TenantCode = row.TenantCode,
        DisplayNameInTenant = row.DisplayNameInTenant ?? string.Empty,
        MemberRole = ToMemberRoleText(row.Role),
        MemberStatus = row.Status == MemberStatus.Enabled ? PortalConstants.Frontend.Enabled : PortalConstants.Frontend.Disabled,
        JoinAt = row.JoinAt ?? DateTimeOffset.UtcNow,
        LastLoginAt = row.LastLoginAt,
    };

    /// <summary>成员角色 → 前端字面量。</summary>
    private static string ToMemberRoleText(TenantMemberRole role) => role switch
    {
        TenantMemberRole.TenantAdmin => PortalConstants.Frontend.TenantAdmin,
        TenantMemberRole.PendingInvite => PortalConstants.Frontend.PendingInvite,
        _ => PortalConstants.Frontend.OrdinaryMember,
    };
}
