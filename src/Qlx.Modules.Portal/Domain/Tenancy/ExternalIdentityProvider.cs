/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 修改时间：2026-10-24
 * 描述：外部身份提供方（SSO/OAuth2 Client）配置实体，表 por_external_identity_providers；
 *       凭据以 Data Protection 密文落库，编码由 IEncodingGenerator 或固定配置提供
 */

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Portal.Domain.Tenancy;

/// <summary>
/// 外部身份提供方类型（决定后续认证处理器接入方式）。
/// </summary>
public enum ExternalIdentityProviderType
{
    /// <summary>企业微信。</summary>
    WeCom = 1,

    /// <summary>钉钉。</summary>
    DingTalk = 2,

    /// <summary>ADFS / 企业自建 SAML-OIDC 网关。</summary>
    Adfs = 3,

    /// <summary>标准 OIDC / OAuth2。</summary>
    Oidc = 4,
}

/// <summary>
/// 外部身份提供方配置（表前缀 por_）。门户级全局配置，一行对应一个可用的 OAuth2/OIDC 客户端。
/// <para>
/// 凭据字段 <see cref="ClientSecretCipher"/> 只存 Data Protection 密文，禁止明文落库；
/// 启动期由加载器解密后装配到内存注册表，未配置任何行时不注册、不报错。
/// </para>
/// </summary>
[Table("por_external_identity_providers")]
[Index(nameof(ProviderCode), IsUnique = true)]
[Index(nameof(IsEnabled), nameof(SortOrder))]
public sealed class ExternalIdentityProvider : Qlx.Abstractions.Domain.AuditableEntity
{
    /// <summary>提供方编码（全局唯一，创建后不可变；由 IEncodingGenerator 或配置固定码提供）。</summary>
    [MaxLength(64)]
    public string ProviderCode { get; private set; } = string.Empty;

    /// <summary>显示名称（登录页按钮文案）。</summary>
    [MaxLength(128)]
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>提供方类型。</summary>
    public ExternalIdentityProviderType ProviderType { get; private set; }

    /// <summary>授权中心地址（绝对 URI，如 https://login.microsoftonline.com/{tenant}/v2.0）。</summary>
    [MaxLength(512)]
    public string Authority { get; private set; } = string.Empty;

    /// <summary>客户端标识（ClientId）。</summary>
    [MaxLength(256)]
    public string ClientId { get; private set; } = string.Empty;

    /// <summary>客户端密钥密文（Data Protection 加密；公共客户端可为空串）。</summary>
    [MaxLength(2048)]
    public string ClientSecretCipher { get; private set; } = string.Empty;

    /// <summary>授权范围（空格或逗号分隔，如 openid profile email）。</summary>
    [MaxLength(1024)]
    public string? Scopes { get; private set; }

    /// <summary>是否启用（停用后启动期不注册）。</summary>
    public bool IsEnabled { get; private set; } = true;

    /// <summary>展示排序（升序）。</summary>
    public int SortOrder { get; private set; }

    /// <summary>
    /// EF Core 无参构造。
    /// </summary>
    public ExternalIdentityProvider() { }

    /// <summary>
    /// 创建外部身份提供方配置。
    /// </summary>
    /// <param name="providerCode">提供方编码（全局唯一）。</param>
    /// <param name="displayName">显示名称。</param>
    /// <param name="providerType">提供方类型。</param>
    /// <param name="authority">授权中心绝对 URI。</param>
    /// <param name="clientId">客户端标识。</param>
    /// <param name="clientSecretCipher">客户端密钥密文（可为空串，公共客户端）。</param>
    /// <param name="scopes">授权范围（空格或逗号分隔）。</param>
    /// <param name="sortOrder">展示排序。</param>
    public ExternalIdentityProvider(
        string providerCode,
        string displayName,
        ExternalIdentityProviderType providerType,
        string authority,
        string clientId,
        string clientSecretCipher,
        string? scopes = null,
        int sortOrder = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(clientSecretCipher);

        ProviderCode = providerCode;
        DisplayName = displayName;
        ProviderType = providerType;
        Authority = authority;
        ClientId = clientId;
        ClientSecretCipher = clientSecretCipher;
        Scopes = scopes;
        SortOrder = sortOrder;
    }

    /// <summary>启用该提供方。</summary>
    public void Enable() => IsEnabled = true;

    /// <summary>停用该提供方（启动期不注册，登录页不展示）。</summary>
    public void Disable() => IsEnabled = false;

    /// <summary>替换客户端密钥密文（凭据轮换；明文加密在服务层完成）。</summary>
    /// <param name="clientSecretCipher">新的密钥密文。</param>
    public void SetClientSecretCipher(string clientSecretCipher)
    {
        ArgumentNullException.ThrowIfNull(clientSecretCipher);
        ClientSecretCipher = clientSecretCipher;
    }

    /// <summary>调整展示排序。</summary>
    /// <param name="sortOrder">排序值（升序）。</param>
    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;
}
