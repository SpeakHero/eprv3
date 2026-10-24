/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 描述：认证强类型 Options（Qlx:Portal:Auth 节），ValidateOnStart 启动校验
 */

using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Qlx.Modules.Portal.Auth;

/// <summary>
/// 认证配置 Options（配置节 <see cref="SectionName"/>）。
/// </summary>
/// <remarks>
/// 属性说明：
/// <list type="bullet">
/// <item><see cref="Issuer"/>：JWT iss 声明（必须为合法绝对 URI）。</item>
/// <item><see cref="Audience"/>：JWT aud 声明（API 资源标识）。</item>
/// <item><see cref="AccessTokenLifetimeMinutes"/>：Access Token 有效期（规格 6.5 默认 2 小时）。</item>
/// <item><see cref="RefreshTokenLifetimeDays"/>：Refresh Token 有效期（规格 6.5 默认 7 天）。</item>
/// <item><see cref="DevSeedAdminPassword"/>：开发期种子管理员初始密码（仅 Development 生效；生产走密钥引用）。</item>
/// <item><see cref="MaxPasswordFailAttempts"/>：密码连续错误锁定阈值（参数 X；生产迁数据库参数表）。</item>
/// <item><see cref="PasswordFailWindowMinutes"/>：失败计数窗口时长。</item>
/// </list>
/// </remarks>
public sealed record QlxAuthOptions : IValidatableObject
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Qlx:Portal:Auth";

    /// <summary>认证默认值（配置缺省时的兜底，集中维护避免散落）。</summary>
    public static class Defaults
    {
        /// <summary>默认 JWT 签发者。</summary>
        public const string Issuer = "https://identity.qlx.local";

        /// <summary>默认 JWT 受众。</summary>
        public const string Audience = "erp-api-gateway";

        /// <summary>默认 Access Token 有效期（分钟）。</summary>
        public const int AccessTokenLifetimeMinutes = 120;

        /// <summary>默认 Refresh Token 有效期（天）。</summary>
        public const int RefreshTokenLifetimeDays = 7;

        /// <summary>默认密码连续错误锁定阈值。</summary>
        public const int MaxPasswordFailAttempts = 5;

        /// <summary>默认失败计数窗口（分钟）。</summary>
        public const int PasswordFailWindowMinutes = 15;

        /// <summary>默认密码最小长度。</summary>
        public const int MinPasswordLength = 8;

        /// <summary>默认密码历史禁重次数。</summary>
        public const int PasswordNeverRepeatLastN = 3;

        /// <summary>默认密码历史保留条数。</summary>
        public const int PasswordHistoryRetentionCount = 10;

        /// <summary>默认租户 Token 静默续期宽限期（分钟）。</summary>
        public const int TenantTokenRenewalGraceMinutes = 10;
    }

    /// <summary>JWT 签发者。</summary>
    public string Issuer { get; init; } = Defaults.Issuer;

    /// <summary>JWT 受众（API 资源标识）。</summary>
    public string Audience { get; init; } = Defaults.Audience;

    /// <summary>Access Token 有效期（分钟）。</summary>
    public int AccessTokenLifetimeMinutes { get; init; } = Defaults.AccessTokenLifetimeMinutes;

    /// <summary>Refresh Token 有效期（天）。</summary>
    public int RefreshTokenLifetimeDays { get; init; } = Defaults.RefreshTokenLifetimeDays;

    /// <summary>开发期种子管理员初始密码（仅 Development；生产经 Data Protection 密钥引用注入）。</summary>
    public string? DevSeedAdminPassword { get; init; }

    /// <summary>密码连续错误锁定阈值（业务参数 X，生产应迁数据库参数表在线维护）。</summary>
    public int MaxPasswordFailAttempts { get; init; } = Defaults.MaxPasswordFailAttempts;

    /// <summary>失败计数窗口（分钟），窗口内累计错误达标锁定，窗口结束自动清零。</summary>
    public int PasswordFailWindowMinutes { get; init; } = Defaults.PasswordFailWindowMinutes;

    // ====== 密码复杂度与历史禁重 ======

    /// <summary>密码最小长度。</summary>
    public int MinPasswordLength { get; init; } = Defaults.MinPasswordLength;

    /// <summary>密码是否必须同时包含字母。</summary>
    public bool PasswordRequireLetter { get; init; } = true;

    /// <summary>密码是否必须同时包含数字。</summary>
    public bool PasswordRequireDigit { get; init; } = true;

    /// <summary>密码是否必须同时包含特殊字符（非字母/数字/空白）。</summary>
    public bool PasswordRequireSpecialChar { get; init; }

    /// <summary>密码不能与最近 N 次历史密码相同；0 表示不做历史禁重。</summary>
    public int PasswordNeverRepeatLastN { get; init; } = Defaults.PasswordNeverRepeatLastN;

    /// <summary>密码历史记录保留条数（超过时自动清理最旧的）。</summary>
    public int PasswordHistoryRetentionCount { get; init; } = Defaults.PasswordHistoryRetentionCount;

    /// <summary>租户级 Token 静默续期宽限期（分钟）：Token 过期后仍可在此时长内凭旧 Token 换新 Token。</summary>
    public int TenantTokenRenewalGraceMinutes { get; init; } = Defaults.TenantTokenRenewalGraceMinutes;

    /// <summary>生产签名/加密证书配置（仅生产环境消费；开发期自动使用开发证书）。</summary>
    public ProductionCertificateOptions Certificate { get; init; } = new();

    /// <summary>生产证书来源（PFX 文件 / 证书存储指纹二选一）。</summary>
    public sealed record ProductionCertificateOptions
    {
        /// <summary>来源：PFX 文件。</summary>
        public const string SourceFile = "File";

        /// <summary>来源：Windows 证书存储。</summary>
        public const string SourceStore = "Store";

        /// <summary>默认证书存储名。</summary>
        public const string DefaultStoreName = "My";

        /// <summary>默认证书存储位置。</summary>
        public const string DefaultStoreLocation = "LocalMachine";

        /// <summary>来源类型（File / Store；空表示未配置，生产启动即失败）。</summary>
        public string Source { get; init; } = string.Empty;

        /// <summary>PFX 文件路径（Source=File 必填）。</summary>
        public string Path { get; init; } = string.Empty;

        /// <summary>PFX 密码（生产经部署密钥注入，禁止明文入库版本管理）。</summary>
        public string Password { get; init; } = string.Empty;

        /// <summary>证书存储名（Source=Store 时使用，默认 My）。</summary>
        public string StoreName { get; init; } = DefaultStoreName;

        /// <summary>证书存储位置（Source=Store 时使用，默认 LocalMachine）。</summary>
        public string StoreLocation { get; init; } = DefaultStoreLocation;

        /// <summary>证书指纹（SHA-1，Source=Store 必填）。</summary>
        public string Thumbprint { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Issuer) || !Uri.TryCreate(Issuer, UriKind.Absolute, out _))
        {
            yield return new ValidationResult("Issuer 必须为合法绝对 URI", [nameof(Issuer)]);
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            yield return new ValidationResult("Audience 不能为空", [nameof(Audience)]);
        }

        if (AccessTokenLifetimeMinutes is < 1 or > 24 * 60)
        {
            yield return new ValidationResult("AccessTokenLifetimeMinutes 必须在 1 ~ 1440 之间", [nameof(AccessTokenLifetimeMinutes)]);
        }

        if (RefreshTokenLifetimeDays is < 1 or > 90)
        {
            yield return new ValidationResult("RefreshTokenLifetimeDays 必须在 1 ~ 90 之间", [nameof(RefreshTokenLifetimeDays)]);
        }

        if (MaxPasswordFailAttempts < 1)
        {
            yield return new ValidationResult("MaxPasswordFailAttempts 必须 ≥ 1", [nameof(MaxPasswordFailAttempts)]);
        }

        if (PasswordFailWindowMinutes < 1)
        {
            yield return new ValidationResult("PasswordFailWindowMinutes 必须 ≥ 1", [nameof(PasswordFailWindowMinutes)]);
        }

        if (MinPasswordLength is < 6 or > 128)
        {
            yield return new ValidationResult("MinPasswordLength 必须在 6 ~ 128 之间", [nameof(MinPasswordLength)]);
        }

        if (PasswordNeverRepeatLastN is < 0 or > 24)
        {
            yield return new ValidationResult("PasswordNeverRepeatLastN 必须在 0 ~ 24 之间（0 表示不做历史禁重）", [nameof(PasswordNeverRepeatLastN)]);
        }

        if (PasswordHistoryRetentionCount < PasswordNeverRepeatLastN)
        {
            yield return new ValidationResult("PasswordHistoryRetentionCount 必须 ≥ PasswordNeverRepeatLastN（否则无法满足禁重窗口）",
                [nameof(PasswordHistoryRetentionCount)]);
        }

        if (TenantTokenRenewalGraceMinutes < 0)
        {
            yield return new ValidationResult("TenantTokenRenewalGraceMinutes 必须 ≥ 0", [nameof(TenantTokenRenewalGraceMinutes)]);
        }

        // 证书节仅在配置了 Source 时做完整性校验（开发期留空合法；生产缺失由 GetProductionCertificate 启动即抛）
        if (string.IsNullOrWhiteSpace(Certificate.Source))
        {
            yield break;
        }

        switch (Certificate.Source.Trim())
        {
            case ProductionCertificateOptions.SourceFile when string.IsNullOrWhiteSpace(Certificate.Path):
                yield return new ValidationResult("Certificate.Source=File 时 Path 必填", [nameof(Certificate)]);
                break;
            case ProductionCertificateOptions.SourceStore when string.IsNullOrWhiteSpace(Certificate.Thumbprint):
                yield return new ValidationResult("Certificate.Source=Store 时 Thumbprint 必填", [nameof(Certificate)]);
                break;
            case not (ProductionCertificateOptions.SourceFile or ProductionCertificateOptions.SourceStore):
                yield return new ValidationResult(
                    $"Certificate.Source 仅支持 {ProductionCertificateOptions.SourceFile} / {ProductionCertificateOptions.SourceStore}",
                    [nameof(Certificate)]);
                break;
        }
    }

    /// <summary>掩码敏感字段，防止日志 / 调试输出泄漏密码（规范 5.5 #34）。</summary>
    public override string ToString()
        => $"{nameof(QlxAuthOptions)} {{ Issuer = {Issuer}, Audience = {Audience}, "
           + $"AccessTokenLifetimeMinutes = {AccessTokenLifetimeMinutes}, RefreshTokenLifetimeDays = {RefreshTokenLifetimeDays}, "
           + $"DevSeedAdminPassword = {(DevSeedAdminPassword is null ? "null" : "***")}, "
           + $"MaxPasswordFailAttempts = {MaxPasswordFailAttempts}, PasswordFailWindowMinutes = {PasswordFailWindowMinutes}, "
           + $"TenantTokenRenewalGraceMinutes = {TenantTokenRenewalGraceMinutes} }}";
}
