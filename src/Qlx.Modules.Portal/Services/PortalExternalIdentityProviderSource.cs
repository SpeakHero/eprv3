/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 修改时间：2026-10-24
 * 描述：外部身份提供方配置来源实现：读 por_external_identity_providers 启用行 + Data Protection 解密凭据
 */

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Qlx.Abstractions.Constants;
using Qlx.Modules.Portal.Domain.Tenancy;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 门户库外部身份提供方配置来源。
/// 凭据列仅存 Data Protection 密文，读取时解密；单行解密 / 配置不合法时记警告并跳过，不影响其余行。
/// </summary>
/// <param name="portalFactory">门户库上下文工厂。</param>
/// <param name="dataProtection">数据保护提供程序（解密客户端密钥）。</param>
/// <param name="logger">日志器。</param>
public sealed partial class PortalExternalIdentityProviderSource(
    IDbContextFactory<PortalDbContext> portalFactory,
    IDataProtectionProvider dataProtection,
    ILogger<PortalExternalIdentityProviderSource> logger) : IExternalIdentityProviderSource
{
    private readonly IDbContextFactory<PortalDbContext> _portalFactory = portalFactory;
    private readonly IDataProtector _protector =
        dataProtection.CreateProtector(QlxDataProtectionPurposes.ExternalIdentityProviderCredentials);
    private readonly ILogger<PortalExternalIdentityProviderSource> _logger = logger;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalIdentityProviderDescriptor>> GetEnabledAsync(
        CancellationToken cancellationToken)
    {
        await using var db = await _portalFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entities = await db.ExternalIdentityProviders
            .AsNoTracking()
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.ProviderCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var providers = new List<ExternalIdentityProviderDescriptor>(entities.Count);
        foreach (var entity in entities)
        {
            string secret;
            try
            {
                secret = string.IsNullOrEmpty(entity.ClientSecretCipher)
                    ? string.Empty
                    : _protector.Unprotect(entity.ClientSecretCipher);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogSecretDecryptFailed(entity.ProviderCode, ex);
                continue;
            }

            var descriptor = ExternalIdentityProviderDescriptor.TryCreate(entity, secret, out var error);
            if (descriptor is null)
            {
                LogInvalidConfiguration(entity.ProviderCode, error ?? "未知原因");
                continue;
            }

            providers.Add(descriptor);
        }

        return providers;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "外部身份提供方 {ProviderCode} 客户端密钥解密失败，已跳过该配置")]
    private partial void LogSecretDecryptFailed(string providerCode, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "外部身份提供方 {ProviderCode} 配置不合法，已跳过：{Reason}")]
    private partial void LogInvalidConfiguration(string providerCode, string reason);
}
