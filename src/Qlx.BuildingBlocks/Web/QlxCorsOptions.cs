/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 20:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 20:30:00 +08:00 中国标准时间 星期三
 * 描述：跨域资源共享（CORS）强类型 Options，配置节 Qlx:Cors
 */

using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Constants;

namespace Qlx.BuildingBlocks.Web;

/// <summary>
/// CORS 配置 Options（配置节 <see cref="SectionName"/>）。
/// </summary>
/// <remarks>
/// 属性说明：
/// <list type="bullet">
/// <item><see cref="AllowedOrigins"/>：允许的前端源（协议+主机+端口，如 http://localhost:5173）；"*" 表示允许任意源（生产慎用）。</item>
/// <item><see cref="AllowedMethods"/>：允许的 HTTP 方法；"*" 表示全部。</item>
/// <item><see cref="AllowedHeaders"/>：允许的请求头；"*" 表示全部。</item>
/// <item><see cref="ExposedHeaders"/>：暴露给前端的响应头（如 Authorization、X-Tenant-Id）。</item>
/// <item><see cref="AllowCredentials"/>：是否允许携带凭证（Cookie / Authorization 头；设为 true 时 AllowedOrigins 不能为 "*"）。</item>
/// <item><see cref="PreflightMaxAgeSeconds"/>：预检请求缓存时长（秒）。</item>
/// </list>
/// </remarks>
public sealed record QlxCorsOptions : IValidatableObject
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Qlx:Cors";

    /// <summary>策略名（用于 AddCors/UseCors 命名策略）。</summary>
    public const string PolicyName = "QlxCorsPolicy";

    /// <summary>开发期默认前端源（生产必须在配置中覆盖为正式域名）。</summary>
    public const string DefaultDevelopmentOrigin = "http://localhost:5173";

    /// <summary>允许的源列表；"*" 表示允许任意源。</summary>
    public string[] AllowedOrigins { get; init; } = [DefaultDevelopmentOrigin];

    /// <summary>允许的 HTTP 方法；"*" 表示全部。</summary>
    public string[] AllowedMethods { get; init; } = ["*"];

    /// <summary>允许的请求头；"*" 表示全部。</summary>
    public string[] AllowedHeaders { get; init; } = ["*"];

    /// <summary>暴露给前端的响应头。</summary>
    public string[] ExposedHeaders { get; init; } =
        [QlxHostConstants.AuthorizationHeaderName, QlxHostConstants.TenantHeaderName, QlxHostConstants.ContentDispositionHeaderName];

    /// <summary>是否允许携带凭证；为 true 时 AllowedOrigins 不能包含 "*"。</summary>
    public bool AllowCredentials { get; init; } = true;

    /// <summary>预检请求缓存时长（秒），0 表示不缓存。</summary>
    public int PreflightMaxAgeSeconds { get; init; } = 600;

    /// <summary>是否启用 CORS（false 时不注册策略，适合纯同源部署）。</summary>
    public bool Enabled { get; init; } = true;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AllowedOrigins is null || AllowedOrigins.Length == 0)
        {
            yield return new ValidationResult("AllowedOrigins 不能为空", [nameof(AllowedOrigins)]);
        }

        if (AllowCredentials && AllowedOrigins is not null && AllowedOrigins.Contains("*"))
        {
            yield return new ValidationResult(
                "AllowCredentials=true 时 AllowedOrigins 不能包含 \"*\"（浏览器安全限制）",
                [nameof(AllowedOrigins), nameof(AllowCredentials)]);
        }

        if (PreflightMaxAgeSeconds < 0)
        {
            yield return new ValidationResult("PreflightMaxAgeSeconds 必须 ≥ 0", [nameof(PreflightMaxAgeSeconds)]);
        }
    }
}
