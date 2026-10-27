/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-21 18:00:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-26 21:40:00 +08:00 中国标准时间 星期一
 * 描述：门户管理端 DTO 契约：对齐前端 src/Qlx.Web/src/types/admin.ts 字段形态（Id 一律 Hashids 混淆串，含数据库模式）
 *       校验分层约定：入参静态规则走 DataAnnotations（格式复用 Qlx.BuildingBlocks 预编译正则特性，
 *       字面量模式统一定义在 PortalConstants.Validation）；配置驱动的密码复杂度经 IValidatableObject
 *       读取 QlxAuthOptions 执行 PasswordPolicyValidator 统一规则；出参为服务端构造，不挂验证注解。
 */

using Microsoft.Extensions.Options;

using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Validation;

using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Services;

using System.ComponentModel.DataAnnotations;

namespace Qlx.Modules.Portal.Dtos;

/// <summary>租户列表查询参数（keyword 匹配名称/编码/税号；status 为前端状态字面量；databaseMode Shared/Isolated 可选）。</summary>
public sealed class PortalTenantQueryRequest : PagedRequest
{
    /// <summary>关键字（名称 / 编码 / 统一社会信用代码）。</summary>
    public string? Keyword { get; set; }

    /// <summary>状态过滤（Enabled / Disabled / Pending / ProvisioningFailed / Expired，空为全部）。</summary>
    [RegularExpression(PortalConstants.Validation.TenantStatusFilterPattern, ErrorMessage = "无效的租户状态过滤值（须为 Enabled / Disabled / Pending / ProvisioningFailed / Expired）")]
    public string? Status { get; set; }

    /// <summary>数据库模式过滤（Shared / Isolated，空为全部）。</summary>
    [RegularExpression(PortalConstants.Validation.DatabaseModePattern, ErrorMessage = "数据库模式过滤仅支持 Shared 或 Isolated")]
    public string? DatabaseMode { get; set; }
}

/// <summary>门户用户列表查询参数。</summary>
public sealed class PortalUserQueryRequest : PagedRequest
{
    /// <summary>关键字（登录账号 / 显示名 / 手机号 / 邮箱）。</summary>
    public string? Keyword { get; set; }

    /// <summary>状态过滤（Enabled / Disabled，空为全部）。</summary>
    [RegularExpression(PortalConstants.Validation.UserStatusFilterPattern, ErrorMessage = "无效的用户状态过滤值（须为 Enabled / Disabled）")]
    public string? Status { get; set; }
}

/// <summary>新建租户入参（门户自动登记数据库 + 初始租户管理员 + 默认模块许可）。</summary>
public sealed record CreateTenantRequest
{
    /// <summary>企业全称。</summary>
    [Required(ErrorMessage = "企业全称必填")]
    [StringLength(PortalConstants.Validation.MaxTenantNameLength, ErrorMessage = "企业全称不能超过 {1} 字")]
    public required string Name { get; init; }

    /// <summary>统一社会信用代码（全局唯一，18 位大写字母或数字）。</summary>
    [Required(ErrorMessage = "统一社会信用代码必填")]
    [CnUscc]
    public required string TaxNo { get; init; }

    /// <summary>会员等级（Trial / Standard / Professional / Flagship，精确大小写）。</summary>
    [Required(ErrorMessage = "会员等级必填")]
    [RegularExpression(PortalConstants.Validation.MemberLevelPattern, ErrorMessage = "会员等级须为 Trial / Standard / Professional / Flagship")]
    public required string MemberLevel { get; init; }

    /// <summary>初始租户管理员登录账号（已存在则建立成员关系，不存在则创建待激活账号）。</summary>
    [Required(ErrorMessage = "初始管理员登录账号必填")]
    [StringLength(PortalConstants.Validation.MaxAdminAccountLength, ErrorMessage = "初始管理员登录账号不能超过 {1} 字")]
    public required string AdminAccount { get; init; }

    /// <summary>
    /// 数据库模式（Shared / Isolated）；为空时取 <c>Qlx:Portal:Provisioning:DefaultDatabaseMode</c>（默认 Shared）。
    /// </summary>
    [RegularExpression(PortalConstants.Validation.DatabaseModePattern, ErrorMessage = "数据库模式仅支持 Shared 或 Isolated")]
    public string? DatabaseMode { get; init; }
}

/// <summary>保存租户模块授权入参（整单提交，按差异落授权记录；空集合表示全部回收）。</summary>
public sealed record SaveTenantLicenseRequest
{
    /// <summary>授权模块编码集合（以本次提交为准，元素须为模块目录已有编码）。</summary>
    [Required(ErrorMessage = "模块编码集合必填")]
    public required IReadOnlyList<string> ModuleCodes { get; init; }

    /// <summary>变更备注（写入授权记录，≤512 字）。</summary>
    [StringLength(PortalConstants.Validation.MaxLicenseRemarkLength, ErrorMessage = "变更备注不能超过 {1} 字")]
    public string? Remark { get; init; }
}

/// <summary>
/// 重置门户用户密码入参。
/// <para>
/// 密码复杂度规则（最小长度 / 字母 / 数字 / 特殊字符）由 <see cref="QlxAuthOptions"/> 运行时配置驱动，
/// 无法用静态注解表达，故经 <see cref="IValidatableObject"/> 在验证管线内解析密码选项并复用
/// <see cref="PasswordPolicyValidator"/> 统一规则（严格以 Qlx:Portal:Auth 配置为准）。
/// 项目验证管线为同步 MVC 模型验证（无异步验证基础设施），故采用同步 <see cref="IValidatableObject"/>。
/// </para>
/// </summary>
public sealed record ResetPortalUserPasswordRequest : IValidatableObject
{
    /// <summary>新密码（复杂度规则取 Qlx:Portal:Auth 密码配置）。</summary>
    [Required(ErrorMessage = "新密码必填")]
    public required string NewPassword { get; init; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // 空值已由 [Required] 管辖，避免同一问题重复报错
        if (string.IsNullOrWhiteSpace(NewPassword))
        {
            yield break;
        }

        var options = validationContext.GetService(typeof(IOptions<QlxAuthOptions>)) as IOptions<QlxAuthOptions>
            ?? throw new InvalidOperationException("密码策略配置（IOptions<QlxAuthOptions>）不可用，无法完成密码校验");

        if (PasswordPolicyValidator.TryGetComplexityError(NewPassword, options.Value, out var error))
        {
            yield return new ValidationResult(error, [nameof(NewPassword)]);
        }
    }
}

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
