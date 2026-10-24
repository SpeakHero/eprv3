/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 修改时间：2026-10-24
 * 描述：外部身份提供方运行期描述符 + 配置映射（实体 → 描述符，含 URI 与范围校验）
 */

using Qlx.Modules.Portal.Domain.Tenancy;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 外部身份提供方运行期描述符（已解密凭据、已校验配置，供登录页展示与后续认证处理器装配）。
/// </summary>
/// <param name="ProviderCode">提供方编码。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="ProviderType">提供方类型。</param>
/// <param name="Authority">授权中心绝对地址。</param>
/// <param name="ClientId">客户端标识。</param>
/// <param name="ClientSecret">客户端密钥明文（公共客户端为空串；仅驻内存，禁止落库/日志）。</param>
/// <param name="Scopes">授权范围（已拆分去重）。</param>
public sealed record ExternalIdentityProviderDescriptor(
    string ProviderCode,
    string DisplayName,
    ExternalIdentityProviderType ProviderType,
    Uri Authority,
    string ClientId,
    string ClientSecret,
    IReadOnlyList<string> Scopes)
{
    /// <summary>
    /// 将配置实体映射为描述符；配置不合法时返回 <c>null</c> 并给出原因（调用方记警告后跳过该行）。
    /// </summary>
    /// <param name="entity">配置实体。</param>
    /// <param name="clientSecret">已解密客户端密钥（可为空串）。</param>
    /// <param name="error">不合法原因（合法时为 <c>null</c>）。</param>
    /// <returns>描述符；配置不合法时为 <c>null</c>。</returns>
    public static ExternalIdentityProviderDescriptor? TryCreate(
        ExternalIdentityProvider entity, string clientSecret, out string? error)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(clientSecret);

        if (!Uri.TryCreate(entity.Authority, UriKind.Absolute, out var authority)
            || (authority.Scheme != Uri.UriSchemeHttps && authority.Scheme != Uri.UriSchemeHttp))
        {
            error = $"Authority 必须为绝对 http/https 地址（当前：{entity.Authority}）";
            return null;
        }

        error = null;
        return new ExternalIdentityProviderDescriptor(
            entity.ProviderCode,
            entity.DisplayName,
            entity.ProviderType,
            authority,
            entity.ClientId,
            clientSecret,
            SplitScopes(entity.Scopes));
    }

    /// <summary>拆分授权范围（空格 / 逗号 / 分号分隔，去重保序）。</summary>
    /// <param name="scopes">原始范围字符串。</param>
    /// <returns>范围集合；为空时返回空集合。</returns>
    private static IReadOnlyList<string> SplitScopes(string? scopes)
    {
        if (string.IsNullOrWhiteSpace(scopes))
        {
            return [];
        }

        return [.. scopes
            .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)];
    }
}
