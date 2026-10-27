/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-21 18:50:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-26 23:20:00 +08:00 中国标准时间 星期一
 * 描述：门户管理端服务契约：租户 / 端口用户 / 模块目录 / 工作台概览
 *       入参校验约定：仅含 DataAnnotations 特性的 DTO 在接口参数上标 [Validate]，实现 IValidatableObject /
 *       IAsyncValidatableObject 的 DTO 由 ValidationDispatchProxy 按接口自动识别，均无需服务实现手写校验调用
 */

using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Validation;
using Qlx.Modules.Portal.Dtos;

namespace Qlx.Modules.Portal.Services;

/// <summary>租户管理服务。</summary>
public interface IPortalTenantAdminService
{
    /// <summary>分页查询租户。</summary>
    Task<PagedResult<PortalTenantItem>> QueryAsync(
        PortalTenantQueryRequest request, CancellationToken cancellationToken);

    /// <summary>新建租户（登记数据库 + 默认许可 + 初始租户管理员）。</summary>
    Task<PortalTenantItem> CreateAsync([Validate] CreateTenantRequest request, CancellationToken cancellationToken);

    /// <summary>启用 / 停用租户（Active ↔ Disabled）。</summary>
    Task<PortalTenantItem> ToggleStatusAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>租户详情（含许可变更记录）。</summary>
    Task<PortalTenantDetailResponse> GetDetailAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>保存租户模块授权（整单提交，按差异落记录）。</summary>
    Task SaveLicenseAsync(string tenantId, [Validate] SaveTenantLicenseRequest request, CancellationToken cancellationToken);

    /// <summary>查询租户成员列表（详情页只读）。</summary>
    Task<IReadOnlyList<TenantMembershipItem>> ListMembersAsync(string tenantId, CancellationToken cancellationToken);
}

/// <summary>门户用户管理服务。</summary>
public interface IPortalUserAdminService
{
    /// <summary>分页查询门户用户。</summary>
    Task<PagedResult<PortalUserItem>> QueryAsync(
        PortalUserQueryRequest request, CancellationToken cancellationToken);

    /// <summary>启用 / 停用门户用户（门户管理员禁止停用）。</summary>
    Task<PortalUserItem> ToggleStatusAsync(string userId, CancellationToken cancellationToken);

    /// <summary>重置门户用户密码（递增 token_version 使全端失效）。</summary>
    /// <remarks>request 实现 IValidatableObject，由校验代理按接口自动识别（密码策略经代理上下文解析 QlxAuthOptions），无需 [Validate] 标记。</remarks>
    Task ResetPasswordAsync(
        string userId, ResetPortalUserPasswordRequest request, CancellationToken cancellationToken);

    /// <summary>用户详情（成员关系 + 登录日志）。</summary>
    Task<PortalUserDetailResponse> GetDetailAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>模块目录与许可记录服务。</summary>
public interface IPortalModuleAdminService
{
    /// <summary>模块目录（含授权租户数与功能点）。</summary>
    Task<IReadOnlyList<PortalModuleItem>> ListModulesAsync(CancellationToken cancellationToken);

    /// <summary>全平台许可变更记录（最近 100 条）。</summary>
    Task<IReadOnlyList<LicenseRecordItem>> ListLicenseRecordsAsync(CancellationToken cancellationToken);
}

/// <summary>门户管理工作台概览服务。</summary>
public interface IPortalOverviewService
{
    /// <summary>工作台指标卡 + 待办。</summary>
    Task<PortalWorkbenchOverviewResponse> GetOverviewAsync(CancellationToken cancellationToken);
}
