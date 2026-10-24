/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24 10:00:00 +08:00 中国标准时间 星期六
 * 描述：租户安全基线强类型 Options（Qlx:Tenant:Security 节）：密码策略 / 会话超时 / 登录锁定 / 上传限制，
 *       启动期 ValidateOnStart 校验（数值范围 + 跨字段约束）
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.BuildingBlocks.Configuration;

/// <summary>
/// 租户安全基线配置（配置节 <see cref="SectionName"/>）。
/// </summary>
/// <remarks>
/// <para>
/// 校验说明：<c>ValidateDataAnnotations</c> 只对根对象执行数据注解校验、不递归子对象，
/// 故全部数值范围与跨字段约束集中在 <see cref="Validate"/> 中显式校验（失败消息带配置键路径）。
/// </para>
/// <para>属性说明：</para>
/// <list type="bullet">
/// <item><see cref="Password"/>：密码复杂度、有效期与历史禁重深度。</item>
/// <item><see cref="Session"/>：会话空闲超时与绝对超时（分钟）。</item>
/// <item><see cref="Lockout"/>：登录失败锁定阈值、计数窗口与锁定时长。</item>
/// <item><see cref="Upload"/>：上传单文件大小上限与扩展名白名单。</item>
/// </list>
/// </remarks>
public sealed record TenantSecurityOptions : IValidatableObject
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Qlx:Tenant:Security";

    /// <summary>密码策略。</summary>
    public PasswordPolicyOptions Password { get; init; } = new();

    /// <summary>会话超时策略。</summary>
    public SessionPolicyOptions Session { get; init; } = new();

    /// <summary>登录锁定策略。</summary>
    public LockoutPolicyOptions Lockout { get; init; } = new();

    /// <summary>上传限制。</summary>
    /// <remarks>
    /// 可写：配置绑定对集合属性为「追加」语义（默认项 + 配置项会合并），故
    /// <see cref="UploadPolicyOptions.AllowedExtensions"/> 绑定前默认留空，
    /// 由 <see cref="SecurityServiceCollectionExtensions.AddQlxTenantSecurity"/> 的 PostConfigure 在未配置时回填默认白名单。
    /// </remarks>
    public UploadPolicyOptions Upload { get; set; } = new();

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Password.MinLength is < 8 or > 128)
        {
            yield return new ValidationResult(
                $"Qlx:Tenant:Security:Password:MinLength 必须在 8 到 128 之间（当前值：{Password.MinLength}）",
                [nameof(Password)]);
        }

        if (Password.ExpiryDays is < 0 or > 3650)
        {
            yield return new ValidationResult(
                $"Qlx:Tenant:Security:Password:ExpiryDays 必须在 0 到 3650 之间（0 表示永不过期，当前值：{Password.ExpiryDays}）",
                [nameof(Password)]);
        }

        if (Password.HistoryDepth is < 0 or > 24)
        {
            yield return new ValidationResult(
                $"Qlx:Tenant:Security:Password:HistoryDepth 必须在 0 到 24 之间（0 表示不做历史禁重，当前值：{Password.HistoryDepth}）",
                [nameof(Password)]);
        }

        if (Session.IdleTimeoutMinutes is < 1 or > 1440)
        {
            yield return new ValidationResult(
                $"Qlx:Tenant:Security:Session:IdleTimeoutMinutes 必须在 1 到 1440 之间（当前值：{Session.IdleTimeoutMinutes}）",
                [nameof(Session)]);
        }

        if (Session.AbsoluteTimeoutMinutes is < 1 or > 43200)
        {
            yield return new ValidationResult(
                $"Qlx:Tenant:Security:Session:AbsoluteTimeoutMinutes 必须在 1 到 43200 之间（当前值：{Session.AbsoluteTimeoutMinutes}）",
                [nameof(Session)]);
        }

        if (Session.IdleTimeoutMinutes >= Session.AbsoluteTimeoutMinutes)
        {
            yield return new ValidationResult(
                "Qlx:Tenant:Security:Session:IdleTimeoutMinutes 必须小于 AbsoluteTimeoutMinutes",
                [nameof(Session)]);
        }

        if (Lockout.MaxFailedAttempts is < 1 or > 100)
        {
            yield return new ValidationResult(
                $"Qlx:Tenant:Security:Lockout:MaxFailedAttempts 必须在 1 到 100 之间（当前值：{Lockout.MaxFailedAttempts}）",
                [nameof(Lockout)]);
        }

        if (Lockout.FailureWindowMinutes is < 1 or > 1440)
        {
            yield return new ValidationResult(
                $"Qlx:Tenant:Security:Lockout:FailureWindowMinutes 必须在 1 到 1440 之间（当前值：{Lockout.FailureWindowMinutes}）",
                [nameof(Lockout)]);
        }

        if (Lockout.LockoutMinutes is < 1 or > 1440)
        {
            yield return new ValidationResult(
                $"Qlx:Tenant:Security:Lockout:LockoutMinutes 必须在 1 到 1440 之间（当前值：{Lockout.LockoutMinutes}）",
                [nameof(Lockout)]);
        }

        if (Upload.MaxFileSizeBytes is < 1024 or > 10_737_418_240)
        {
            yield return new ValidationResult(
                $"Qlx:Tenant:Security:Upload:MaxFileSizeBytes 必须在 1024 到 10737418240 之间（当前值：{Upload.MaxFileSizeBytes}）",
                [nameof(Upload)]);
        }

        if (Upload.AllowedExtensions is not { Count: > 0 })
        {
            yield return new ValidationResult(
                "Qlx:Tenant:Security:Upload:AllowedExtensions 不能为空",
                [nameof(Upload)]);
        }
        else
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var extension in Upload.AllowedExtensions)
            {
                if (string.IsNullOrWhiteSpace(extension)
                    || !extension.StartsWith('.')
                    || extension.Any(char.IsUpper))
                {
                    yield return new ValidationResult(
                        $"Qlx:Tenant:Security:Upload:AllowedExtensions 的每一项必须以 '.' 开头且全部小写（非法项：\"{extension}\"）",
                        [nameof(Upload)]);
                    continue;
                }

                if (!seen.Add(extension))
                {
                    yield return new ValidationResult(
                        $"Qlx:Tenant:Security:Upload:AllowedExtensions 不得包含重复项（大小写不敏感）：\"{extension}\"",
                        [nameof(Upload)]);
                }
            }
        }
    }

    /// <summary>
    /// 密码策略：复杂度要求、有效期与历史禁重深度。
    /// </summary>
    public sealed record PasswordPolicyOptions
    {
        /// <summary>密码最小长度（8 ~ 128）。</summary>
        public int MinLength { get; init; } = 10;

        /// <summary>是否必须包含大写字母。</summary>
        public bool RequireUppercase { get; init; } = true;

        /// <summary>是否必须包含小写字母。</summary>
        public bool RequireLowercase { get; init; } = true;

        /// <summary>是否必须包含数字。</summary>
        public bool RequireDigit { get; init; } = true;

        /// <summary>是否必须包含特殊符号。</summary>
        public bool RequireSymbol { get; init; } = true;

        /// <summary>密码有效期（天），0 表示永不过期。</summary>
        public int ExpiryDays { get; init; } = 90;

        /// <summary>历史密码禁重深度（最近 N 次不得重复），0 表示不做历史禁重。</summary>
        public int HistoryDepth { get; init; } = 5;
    }

    /// <summary>
    /// 会话超时策略（分钟）。
    /// </summary>
    public sealed record SessionPolicyOptions
    {
        /// <summary>空闲超时（分钟）：无操作即失效。</summary>
        public int IdleTimeoutMinutes { get; init; } = 30;

        /// <summary>绝对超时（分钟）：登录后累计时长上限，必须大于空闲超时。</summary>
        public int AbsoluteTimeoutMinutes { get; init; } = 720;
    }

    /// <summary>
    /// 登录锁定策略。
    /// </summary>
    public sealed record LockoutPolicyOptions
    {
        /// <summary>锁定阈值（窗口内连续失败次数），与 por_parameters 的 auth.max_password_fail_attempts 对齐。</summary>
        public int MaxFailedAttempts { get; init; } = 5;

        /// <summary>失败计数窗口（分钟），与 por_parameters 的 auth.password_fail_window_minutes 对齐。</summary>
        public int FailureWindowMinutes { get; init; } = 15;

        /// <summary>锁定时长（分钟）。</summary>
        public int LockoutMinutes { get; init; } = 30;
    }

    /// <summary>
    /// 上传限制。
    /// </summary>
    public sealed record UploadPolicyOptions
    {
        /// <summary>单文件大小上限（字节），默认 100MB，与 <c>Qlx:Support:Files:MaxSyncUploadBytes</c> 默认值一致。</summary>
        public long MaxFileSizeBytes { get; init; } = 104_857_600;

        /// <summary>
        /// 允许的扩展名白名单（小写、以 '.' 开头、不得重复）。
        /// </summary>
        /// <remarks>
        /// 绑定前默认留空：.NET 配置绑定对集合属性为追加语义（默认项 + 配置项合并），
        /// 若此处预置默认值会导致配置文件同名白名单被重复追加；
        /// 默认值见 <see cref="DefaultAllowedExtensions"/>，未配置时由 PostConfigure 回填。
        /// </remarks>
        public IReadOnlyList<string> AllowedExtensions { get; init; } = [];

        /// <summary>
        /// 默认扩展名白名单（配置缺省时回填，与 <c>config/tenant/security.json</c> 保持一致）。
        /// </summary>
        public static IReadOnlyList<string> DefaultAllowedExtensions { get; } =
        [
            ".png",
            ".jpg",
            ".jpeg",
            ".gif",
            ".webp",
            ".pdf",
            ".doc",
            ".docx",
            ".xls",
            ".xlsx",
            ".ppt",
            ".pptx",
            ".txt",
            ".csv",
            ".zip",
        ];
    }
}
