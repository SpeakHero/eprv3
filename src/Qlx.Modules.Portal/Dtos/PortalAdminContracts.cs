/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 18:00:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 22:30:00 +08:00 中国标准时间 星期三
 * 描述：门户管理端 DTO 契约：对齐前端 src/Qlx.Web/src/types/admin.ts 字段形态（Id 一律 Hashids 混淆串，含数据库模式）
 */

using Qlx.BuildingBlocks.Api;

namespace Qlx.Modules.Portal.Dtos;

/// <summary>租户列表查询参数（keyword 匹配名称/编码/税号；status 为前端状态字面量；databaseMode Shared/Isolated 可选）。</summary>
public sealed class PortalTenantQueryRequest : PagedRequest
{
    /// <summary>关键字（名称 / 编码 / 统一社会信用代码）。</summary>
    public string? Keyword { get; set; }

    /// <summary>状态过滤（Enabled / Disabled / Pending / ProvisioningFailed / Expired，空为全部）。</summary>
    public string? Status { get; set; }

    /// <summary>数据库模式过滤（Shared / Isolated，空为全部）。</summary>
    public string? DatabaseMode { get; set; }
}

/// <summary>门户用户列表查询参数。</summary>
public sealed class PortalUserQueryRequest : PagedRequest
{
    /// <summary>关键字（登录账号 / 显示名 / 手机号 / 邮箱）。</summary>
    public string? Keyword { get; set; }

    /// <summary>状态过滤（Enabled / Disabled，空为全部）。</summary>
    public string? Status { get; set; }
}

/// <summary>新建租户入参（门户自动登记数据库 + 初始租户管理员 + 默认模块许可）。</summary>
public sealed record CreateTenantRequest
{
    /// <summary>企业全称。</summary>
    public required string Name { get; init; }

    /// <summary>统一社会信用代码（全局唯一）。</summary>
    public required string TaxNo { get; init; }

    /// <summary>会员等级（Trial / Standard / Professional / Flagship）。</summary>
    public required string MemberLevel { get; init; }

    /// <summary>初始租户管理员登录账号（已存在则建立成员关系，不存在则创建待激活账号）。</summary>
    public required string AdminAccount { get; init; }

    /// <summary>
    /// 数据库模式（Shared / Isolated）；为空时取 <c>Qlx:Portal:Provisioning:DefaultDatabaseMode</c>（默认 Shared）。
    /// </summary>
    public string? DatabaseMode { get; init; }
}

/// <summary>保存租户模块授权入参（整单提交，按差异落授权记录）。</summary>
public sealed record SaveTenantLicenseRequest
{
    /// <summary>授权模块编码集合（以本次提交为准）。</summary>
    public required IReadOnlyList<string> ModuleCodes { get; init; }

    /// <summary>变更备注（写入授权记录）。</summary>
    public string? Remark { get; init; }
}

/// <summary>重置门户用户密码入参。</summary>
public sealed record ResetPortalUserPasswordRequest
{
    /// <summary>新密码（≥8 位且同时含字母与数字）。</summary>
    public required string NewPassword { get; init; }
}

/// <summary>通用分页结果（对齐前端 PagedResult：items + totalCount）。</summary>
public sealed record PortalPagedResult<T>(IReadOnlyList<T> Items, long TotalCount);

/// <summary>租户列表项。</summary>
public sealed record PortalTenantItem
{
    /// <summary>租户 Id（Hashids）。</summary>
    public required string Id { get; init; }

    /// <summary>企业全称。</summary>
    public required string Name { get; init; }

    /// <summary>租户编码。</summary>
    public required string Code { get; init; }

    /// <summary>统一社会信用代码。</summary>
    public required string TaxNo { get; init; }

    /// <summary>租户状态（Enabled / Disabled / Pending / ProvisioningFailed / Expired）。</summary>
    public required string Status { get; init; }

    /// <summary>会员等级（Trial / Standard / Professional / Flagship）。</summary>
    public required string MemberLevel { get; init; }

    /// <summary>会员到期时间（无期限为 null）。</summary>
    public DateTimeOffset? MemberExpireAt { get; init; }

    /// <summary>数据库状态（Ready / Provisioning / Failed）。</summary>
    public required string DbStatus { get; init; }

    /// <summary>数据库模式（Shared / Isolated）。</summary>
    public required string DatabaseMode { get; init; }

    /// <summary>数据库名称（共享租户为租户共享库名；无路由行时为 null）。</summary>
    public string? DbName { get; init; }

    /// <summary>预配失败原因（仅 Failed 时有值）。</summary>
    public string? DbError { get; init; }

    /// <summary>已授权模块编码集合。</summary>
    public required IReadOnlyList<string> LicensedModuleCodes { get; init; }

    /// <summary>创建时间。</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>最近登录时间（成员最近进入租户时间）。</summary>
    public DateTimeOffset? LastLoginAt { get; init; }
}

/// <summary>租户详情响应（租户基本信息 + 数据库摘要 + 模块授权变更记录）。</summary>
public sealed record PortalTenantDetailResponse(
    PortalTenantItem Tenant,
    PortalTenantDatabaseSummary? Database,
    IReadOnlyList<LicenseRecordItem> Records);

/// <summary>租户数据库摘要（凭据不返回，只暴露模式/库名/主机/状态）。</summary>
public sealed record PortalTenantDatabaseSummary
{
    /// <summary>数据库模式（Shared / Isolated）。</summary>
    public required string Mode { get; init; }

    /// <summary>数据库名称。</summary>
    public required string DbName { get; init; }

    /// <summary>数据库主机。</summary>
    public required string DbHost { get; init; }

    /// <summary>数据库端口。</summary>
    public required int DbPort { get; init; }

    /// <summary>数据库状态（Ready / Provisioning / Archived）。</summary>
    public required string Status { get; init; }

    /// <summary>共享池编码（独立库为 default 占位）。</summary>
    public required string PoolCode { get; init; }
}

/// <summary>许可变更记录项。</summary>
public sealed record LicenseRecordItem
{
    /// <summary>记录 Id（Hashids）。</summary>
    public required string Id { get; init; }

    /// <summary>租户 Id（Hashids）。</summary>
    public required string TenantId { get; init; }

    /// <summary>租户名称快照。</summary>
    public required string TenantName { get; init; }

    /// <summary>模块编码。</summary>
    public required string ModuleCode { get; init; }

    /// <summary>模块名称快照。</summary>
    public required string ModuleName { get; init; }

    /// <summary>变更动作（Granted / Revoked / Adjusted）。</summary>
    public required string Action { get; init; }

    /// <summary>操作人显示名。</summary>
    public required string OperatorName { get; init; }

    /// <summary>变更时间。</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>备注。</summary>
    public required string Remark { get; init; }
}

/// <summary>门户用户列表项。</summary>
public sealed record PortalUserItem
{
    /// <summary>用户 Id（Hashids）。</summary>
    public required string Id { get; init; }

    /// <summary>登录账号。</summary>
    public required string LoginAccount { get; init; }

    /// <summary>显示名。</summary>
    public required string DisplayName { get; init; }

    /// <summary>手机号。</summary>
    public required string Phone { get; init; }

    /// <summary>邮箱。</summary>
    public required string Email { get; init; }

    /// <summary>账号状态（Enabled / Disabled）。</summary>
    public required string Status { get; init; }

    /// <summary>是否已实名认证。</summary>
    public required bool RealNameVerified { get; init; }

    /// <summary>是否门户管理员。</summary>
    public required bool IsPortalAdmin { get; init; }

    /// <summary>最近登录时间。</summary>
    public DateTimeOffset? LastLoginAt { get; init; }

    /// <summary>创建时间。</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>门户用户详情响应（用户 + 成员关系 + 登录日志）。</summary>
public sealed record PortalUserDetailResponse(
    PortalUserItem User,
    IReadOnlyList<TenantMembershipItem> Memberships,
    IReadOnlyList<LoginLogItem> Logs);

/// <summary>租户成员关系项。</summary>
public sealed record TenantMembershipItem
{
    /// <summary>成员关系 Id（Hashids）。</summary>
    public required string Id { get; init; }

    /// <summary>门户用户 Id（Hashids）。</summary>
    public required string PortalUserId { get; init; }

    /// <summary>登录账号。</summary>
    public required string LoginAccount { get; init; }

    /// <summary>租户 Id（Hashids）。</summary>
    public required string TenantId { get; init; }

    /// <summary>租户名称。</summary>
    public required string TenantName { get; init; }

    /// <summary>租户编码。</summary>
    public required string TenantCode { get; init; }

    /// <summary>租户内显示名。</summary>
    public required string DisplayNameInTenant { get; init; }

    /// <summary>成员角色（TenantAdmin / OrdinaryMember / PendingInvite）。</summary>
    public required string MemberRole { get; init; }

    /// <summary>成员状态（Enabled / Disabled）。</summary>
    public required string MemberStatus { get; init; }

    /// <summary>加入时间。</summary>
    public required DateTimeOffset JoinAt { get; init; }

    /// <summary>最近进入租户时间。</summary>
    public DateTimeOffset? LastLoginAt { get; init; }
}

/// <summary>模块目录项（含授权租户数）。</summary>
public sealed record PortalModuleItem
{
    /// <summary>模块编码。</summary>
    public required string Code { get; init; }

    /// <summary>模块名称。</summary>
    public required string Name { get; init; }

    /// <summary>当前版本。</summary>
    public required string Version { get; init; }

    /// <summary>模块简介。</summary>
    public required string Description { get; init; }

    /// <summary>功能点（授权菜单名称集合）。</summary>
    public required IReadOnlyList<string> Features { get; init; }

    /// <summary>已授权租户数。</summary>
    public required int LicensedTenantCount { get; init; }
}

/// <summary>登录日志项（数据来自平台日志中心 IAuditService，不落门户库）。</summary>
public sealed record LoginLogItem
{
    /// <summary>日志 Id（Hashids）。</summary>
    public required string Id { get; init; }

    /// <summary>登录账号。</summary>
    public required string LoginAccount { get; init; }

    /// <summary>归属租户（门户登录为「门户管理」）。</summary>
    public required string TenantName { get; init; }

    /// <summary>结果（Success / Failure）。</summary>
    public required string Result { get; init; }

    /// <summary>来源 IP。</summary>
    public required string IpAddress { get; init; }

    /// <summary>时间。</summary>
    public required DateTimeOffset At { get; init; }
}

/// <summary>门户管理工作台指标卡。</summary>
public sealed record PortalAdminStatItem
{
    /// <summary>指标键。</summary>
    public required string Key { get; init; }

    /// <summary>指标名称。</summary>
    public required string Label { get; init; }

    /// <summary>指标值。</summary>
    public required long Value { get; init; }

    /// <summary>单位。</summary>
    public required string Unit { get; init; }

    /// <summary>辅助说明。</summary>
    public required string Hint { get; init; }

    /// <summary>图标编码（前端图标库键）。</summary>
    public required string Icon { get; init; }

    /// <summary>色调（brand / gold / success / warning）。</summary>
    public required string Tone { get; init; }
}

/// <summary>门户管理工作台待办项。</summary>
public sealed record PortalAdminTodoItem
{
    /// <summary>待办标识。</summary>
    public required string Id { get; init; }

    /// <summary>待办类型。</summary>
    public required string Type { get; init; }

    /// <summary>标题。</summary>
    public required string Title { get; init; }

    /// <summary>发生时间。</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>级别（warning / danger / info）。</summary>
    public required string Level { get; init; }

    /// <summary>跳转地址。</summary>
    public required string Href { get; init; }
}

/// <summary>门户管理工作台概览响应。</summary>
public sealed record PortalWorkbenchOverviewResponse(
    IReadOnlyList<PortalAdminStatItem> Stats,
    IReadOnlyList<PortalAdminTodoItem> Todos);
