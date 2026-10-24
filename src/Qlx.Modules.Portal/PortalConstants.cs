/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 20:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 20:00:00 +08:00 中国标准时间 星期四
 * 描述：Portal 模块私有常量集中入口（前端契约字面量、审计操作码、缓存键、校验长度、种子数据）
 */

using Qlx.Abstractions.Constants;

namespace Qlx.Modules.Portal;

/// <summary>
/// Portal 模块私有常量集中入口。仅本模块使用的字面量统一在此维护，
/// 跨模块共享的 Claim 名 / 模块编码 / 主机常量见 <c>Qlx.Abstractions.Constants</c>。
/// </summary>
public static class PortalConstants
{
    /// <summary>前端契约字面量（与页面严格一致，禁止在各服务内散落）。</summary>
    public static class Frontend
    {
        /// <summary>启用状态。</summary>
        public const string Enabled = "Enabled";

        /// <summary>停用状态。</summary>
        public const string Disabled = "Disabled";

        /// <summary>待处理（租户预配中）。</summary>
        public const string Pending = "Pending";

        /// <summary>预配失败。</summary>
        public const string ProvisioningFailed = "ProvisioningFailed";

        /// <summary>已过期（宽限期）。</summary>
        public const string Expired = "Expired";

        /// <summary>数据库就绪。</summary>
        public const string Ready = "Ready";

        /// <summary>数据库迁移中。</summary>
        public const string Provisioning = "Provisioning";

        /// <summary>数据库异常。</summary>
        public const string Failed = "Failed";

        /// <summary>数据库已归档。</summary>
        public const string Archived = "Archived";

        /// <summary>共享库模式。</summary>
        public const string Shared = "Shared";

        /// <summary>独立库模式。</summary>
        public const string Isolated = "Isolated";

        /// <summary>许可授予。</summary>
        public const string Granted = "Granted";

        /// <summary>许可回收。</summary>
        public const string Revoked = "Revoked";

        /// <summary>许可调整。</summary>
        public const string Adjusted = "Adjusted";

        /// <summary>租户管理员角色。</summary>
        public const string TenantAdmin = "TenantAdmin";

        /// <summary>待邀请成员角色。</summary>
        public const string PendingInvite = "PendingInvite";

        /// <summary>普通成员角色。</summary>
        public const string OrdinaryMember = "OrdinaryMember";

        /// <summary>登录成功。</summary>
        public const string Success = "Success";

        /// <summary>登录失败。</summary>
        public const string Failure = "Failure";

        /// <summary>布尔真值（门户管理员标记参数值）。</summary>
        public const string BooleanTrue = "true";
    }

    /// <summary>会员等级（前端可选中值与套餐编码）。</summary>
    public static class MemberLevels
    {
        /// <summary>试用版。</summary>
        public const string Trial = "Trial";

        /// <summary>标准版。</summary>
        public const string Standard = "Standard";

        /// <summary>专业版。</summary>
        public const string Professional = "Professional";

        /// <summary>旗舰版。</summary>
        public const string Flagship = "Flagship";

        /// <summary>会员等级合法取值集合。</summary>
        public static readonly IReadOnlyList<string> All = [Trial, Standard, Professional, Flagship];
    }

    /// <summary>套餐编码（存库小写，与会员等级一一对应）。</summary>
    public static class PlanCodes
    {
        /// <summary>试用版套餐。</summary>
        public const string Trial = "trial";

        /// <summary>标准版套餐。</summary>
        public const string Standard = "standard";

        /// <summary>专业版套餐。</summary>
        public const string Professional = "professional";

        /// <summary>旗舰版套餐。</summary>
        public const string Flagship = "flagship";
    }

    /// <summary>门户审计操作码（平台日志中心 plog_operation）。</summary>
    public static class AuditOperations
    {
        /// <summary>登录成功。</summary>
        public const string LoginSuccess = "LOGIN_SUCCESS";

        /// <summary>登录失败。</summary>
        public const string LoginFailure = "LOGIN_FAILED";

        /// <summary>新建租户。</summary>
        public const string CreateTenant = "CREATE_TENANT";

        /// <summary>租户启停。</summary>
        public const string ToggleTenantStatus = "TOGGLE_TENANT_STATUS";

        /// <summary>保存租户模块许可。</summary>
        public const string SaveTenantLicense = "SAVE_TENANT_LICENSE";

        /// <summary>门户用户启停。</summary>
        public const string ToggleUserStatus = "TOGGLE_USER_STATUS";

        /// <summary>重置门户用户密码。</summary>
        public const string ResetUserPassword = "RESET_USER_PASSWORD";

        /// <summary>发起共享库升级独立库。</summary>
        public const string PromoteToIsolated = "PROMOTE_TO_ISOLATED";

        /// <summary>重试租户升级任务。</summary>
        public const string RetryTenantUpgrade = "RETRY_TENANT_UPGRADE";

        // ======= 门户角色管理域 =======

        /// <summary>新建门户角色。</summary>
        public const string CreatePortalRole = "PORTAL_ROLE_CREATE";

        /// <summary>更新门户角色。</summary>
        public const string UpdatePortalRole = "PORTAL_ROLE_UPDATE";

        /// <summary>删除门户角色。</summary>
        public const string DeletePortalRole = "PORTAL_ROLE_DELETE";

        /// <summary>全量替换角色权限。</summary>
        public const string ReplaceRolePermissions = "PORTAL_ROLE_PERMISSIONS_REPLACE";

        /// <summary>全量替换角色成员。</summary>
        public const string ReplaceRoleMembers = "PORTAL_ROLE_MEMBERS_REPLACE";

        /// <summary>全量替换用户角色（反向路由）。</summary>
        public const string ReplaceUserRoles = "PORTAL_USER_ROLES_REPLACE";
    }

    /// <summary>门户级业务错误码（ConflictException / ValidationException 第二参数）。</summary>
    public static class ErrorCodes
    {
        /// <summary>租户编码已存在。</summary>
        public const string TenantCodeExists = "TENANT_CODE_EXISTS";

        /// <summary>统一社会信用代码已存在。</summary>
        public const string TenantTaxNoExists = "TENANT_TAXNO_EXISTS";

        /// <summary>租户状态不允许当前操作。</summary>
        public const string TenantStatusConflict = "TENANT_STATUS_CONFLICT";

        /// <summary>门户管理员账号受保护。</summary>
        public const string PortalAdminProtected = "PORTAL_ADMIN_PROTECTED";

        /// <summary>企业管理员角色受保护。</summary>
        public const string TenantAdminRoleProtected = "TENANT_ADMIN_ROLE_PROTECTED";

        /// <summary>待邀请成员尚未设置密码。</summary>
        public const string PendingInviteNoPassword = "PENDING_INVITE_NO_PASSWORD";

        /// <summary>成员已停用。</summary>
        public const string MemberDisabled = "MEMBER_DISABLED";

        /// <summary>成员不处于待激活邀请状态。</summary>
        public const string MemberNotPendingInvite = "MEMBER_NOT_PENDING_INVITE";

        /// <summary>数据库模式非法。</summary>
        public const string TenantDatabaseModeInvalid = "TENANT_DATABASE_MODE_INVALID";

        /// <summary>数据库模式未变更。</summary>
        public const string TenantDatabaseModeUnchanged = "TENANT_DATABASE_MODE_UNCHANGED";

        /// <summary>已有进行中的升级任务。</summary>
        public const string TenantUpgradeInProgress = "TENANT_UPGRADE_IN_PROGRESS";

        /// <summary>升级任务不可重试。</summary>
        public const string TenantUpgradeRetryInvalid = "TENANT_UPGRADE_RETRY_INVALID";

        /// <summary>升级任务状态迁移非法。</summary>
        public const string TenantUpgradeTransitionInvalid = "TENANT_UPGRADE_TRANSITION_INVALID";
    }

    /// <summary>门户级缓存键与 TTL（_portal 为保留租户码前缀）。</summary>
    public static class CacheKeys
    {
        /// <summary>模块许可缓存键前缀。</summary>
        public const string LicensePrefix = "_portal:license:";

        /// <summary>系统参数缓存键前缀。</summary>
        public const string SystemParameterPrefix = "_portal:params:";

        /// <summary>门户缓存默认有效期（参数与许可共用）。</summary>
        public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

        /// <summary>账号锁定标记值（登录失败计数达阈值后写入）。</summary>
        public const string LockoutMarker = "locked";
    }

    /// <summary>请求/响应参数名与审计元数据属性名。</summary>
    public static class Parameters
    {
        /// <summary>登录账号请求参数名。</summary>
        public const string LoginAccount = "login_account";

        /// <summary>密码请求参数名。</summary>
        public const string Password = "password";

        /// <summary>进入租户上下文请求参数名。</summary>
        public const string TenantId = "tenant_id";

        /// <summary>进入租户上下文：所选租户角色 Id（混淆串，可空；多角色时必填）。</summary>
        public const string MemberRoleId = "member_role_id";

        /// <summary>登录响应：可进入租户摘要列表。</summary>
        public const string ResponseTenants = "qlx_tenants";

        /// <summary>登录响应：门户管理员标记。</summary>
        public const string ResponsePortalAdmin = "qlx_portal_admin";

        /// <summary>审计元数据：来源 IP 属性名。</summary>
        public const string MetadataIp = "ip";
    }

    /// <summary>OpenIddict 端点与认证头。</summary>
    public static class Endpoints
    {
        /// <summary>Token 端点路径。</summary>
        public const string Token = "/connect/token";

        /// <summary>吊销端点路径。</summary>
        public const string Revocation = "/connect/revoke";

        /// <summary>Bearer 认证头前缀。</summary>
        public const string BearerPrefix = "Bearer ";
    }

    /// <summary>入参校验与查询上限。</summary>
    public static class Validation
    {
        /// <summary>租户编码最小长度。</summary>
        public const int MinTenantCodeLength = 2;

        /// <summary>租户编码最大长度。</summary>
        public const int MaxTenantCodeLength = 64;

        /// <summary>企业全称最大长度。</summary>
        public const int MaxTenantNameLength = 128;

        /// <summary>企业全称（显示名）数据库列长度。</summary>
        public const int TenantNameColumnLength = 128;

        /// <summary>统一社会信用代码长度。</summary>
        public const int TaxNoLength = 18;

        /// <summary>初始管理员登录账号最大长度。</summary>
        public const int MaxAdminAccountLength = 64;

        /// <summary>许可记录查询上限。</summary>
        public const int MaxLicenseRecordTake = 500;

        /// <summary>密码历史查询上限。</summary>
        public const int MaxPasswordHistoryTake = 100;

        /// <summary>密码历史保留上限。</summary>
        public const int MaxPasswordHistoryTrim = 1000;

        /// <summary>租户详情许可记录默认条数。</summary>
        public const int DefaultLicenseRecordLimit = 50;

        /// <summary>登录日志查询默认条数。</summary>
        public const int LoginLogQueryLimit = 100;
    }

    /// <summary>开发期种子数据（仅 Development 环境注册的种子服务使用）。</summary>
    public static class Seed
    {
        /// <summary>开发租户编码。</summary>
        public const string TenantCode = "qlx-dev";

        /// <summary>开发租户名称。</summary>
        public const string TenantName = "淇利馨 ERP 开发租户";

        /// <summary>开发租户简称。</summary>
        public const string TenantShortName = "开发租户";

        /// <summary>开发租户统一社会信用代码。</summary>
        public const string TaxNo = "91510100MA0000000X";

        /// <summary>开发租户行业。</summary>
        public const string Industry = "通用";

        /// <summary>开发租户联系人。</summary>
        public const string ContactName = "开发管理员";

        /// <summary>开发租户联系邮箱。</summary>
        public const string ContactEmail = "dev@qlx.example";

        /// <summary>开发租户套餐编码。</summary>
        public const string PlanCode = "dev-free";

        /// <summary>开发租户订阅年限。</summary>
        public const int SubscriptionYears = 10;

        /// <summary>开发租户数据库名。</summary>
        public const string DatabaseName = "qlx_dev";

        /// <summary>开发门户管理员账号。</summary>
        public const string AdminAccount = "qlxadmin";

        /// <summary>开发门户管理员显示名。</summary>
        public const string AdminDisplayName = "SpeakHero";

        /// <summary>OpenIddict 公共客户端显示名。</summary>
        public const string ClientDisplayName = "ERP 前端单壳";
    }

    /// <summary>租户预配默认值（与配置节默认值保持一致）。</summary>
    public static class ProvisioningDefaults
    {
        /// <summary>默认数据库主机（开发期 LocalDB）。</summary>
        public const string DbHost = QlxDatabaseConstants.LocalDbHostMarker;

        /// <summary>默认数据库端口。</summary>
        public const int DbPort = QlxDatabaseConstants.SqlServerDefaultPort;

        /// <summary>默认独立库名前缀。</summary>
        public const string DbNamePrefix = "qlx_tenant_";

        /// <summary>默认试用版订阅天数。</summary>
        public const int TrialDays = 14;

        /// <summary>默认共享池编码（多共享池预留，本期固定）。</summary>
        public const string DefaultPoolCode = "default";

        /// <summary>默认连接超时（秒）。</summary>
        public const int ConnectTimeoutSeconds = 30;
    }

    /// <summary>异常资源类型名（NotFoundException 第一参数，前端据此定位资源）。</summary>
    public static class ResourceNames
    {
        /// <summary>租户。</summary>
        public const string Tenant = "租户";

        /// <summary>升级任务。</summary>
        public const string UpgradeJob = "升级任务";

        /// <summary>门户用户。</summary>
        public const string PortalUser = "门户用户";

        /// <summary>系统参数。</summary>
        public const string SystemParameter = "系统参数";

        /// <summary>租户成员（内部标识）。</summary>
        public const string TenantMember = "TenantMember";

        /// <summary>门户用户（内部标识）。</summary>
        public const string PortalUserEntity = "PortalUser";
    }

    /// <summary>门户管理界面显示名与操作人兜底值。</summary>
    public static class Labels
    {
        /// <summary>门户身份认证在登录日志中的归属租户名。</summary>
        public const string PortalScopeName = "门户管理";

        /// <summary>门户权限点分组名。</summary>
        public const string PortalAdminGroup = "门户管理";

        /// <summary>身份权限权限点分组名。</summary>
        public const string IdentityGroup = "身份权限";

        /// <summary>操作人兜底（登录账号）。</summary>
        public const string OperatorFallback = "portal-admin";

        /// <summary>操作人兜底（显示名称）。</summary>
        public const string OperatorDisplayFallback = "门户管理员";

        /// <summary>初始租户管理员显示名。</summary>
        public const string InitialAdminDisplayName = "管理员";

        /// <summary>租户登录审计写入失败日志的标签。</summary>
        public const string TenantLoginLogTag = "TENANT_LOGIN";
    }

    /// <summary>Token 黑名单写入值。</summary>
    public const string LogoutBlacklistValue = "logout";

    /// <summary>登录账号缺失时的日志占位值。</summary>
    public const string UnknownValue = "unknown";

    /// <summary>门户管理员 Claim 取值（<c>is_portal_admin=1</c>）。</summary>
    public const string PortalAdminFlagValue = "1";

    /// <summary>预置模块定义版本号（模块种子统一初始版本）。</summary>
    public const string ModuleDefinitionVersion = "1.0.0";
}
