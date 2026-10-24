/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 描述：OpenIddict 装配扩展：核心服务 + Server（Token/Revoke 端点、自定义授权类型）+ 本地验证
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Qlx.Modules.Portal.Auth;

namespace Qlx.Modules.Portal;

/// <summary>
/// 认证体系装配扩展（必须在 <see cref="PortalServiceCollectionExtensions.AddQlxPortal"/> 之后调用）。
/// <para>包含：强类型 Auth Options（ValidateOnStart）、OpenIddict Core/Server/Validation、
/// 密码哈希器、身份映射器、许可服务。控制器由 Host 的 AddControllers 注册。</para>
/// </summary>
public static class OpenIddictConfigurator
{
    /// <summary>前端单壳 SPA 客户端标识（公共客户端，无密钥）。</summary>
    public const string SpaClientId = "erp-web";

    /// <summary>
    /// 注册认证体系组件。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置根（绑定 <see cref="QlxAuthOptions.SectionName"/> 节）。</param>
    /// <param name="environment">Host 环境（决定开发期开发签名证书 / 生产证书校验）。</param>
    public static IServiceCollection AddQlxAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        // 1. 强类型 Options + 启动校验（项目铁律：record + init + ValidateOnStart）
        services.AddOptions<QlxAuthOptions>()
            .BindConfiguration(QlxAuthOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var auth = configuration.GetSection(QlxAuthOptions.SectionName).Get<QlxAuthOptions>()
            ?? throw new InvalidOperationException($"缺少配置节 {QlxAuthOptions.SectionName}");

        // 2. OpenIddict 核心（EF 存储挂 PortalDbContext，por_oidc_* 表）
        services.AddOpenIddict()
            .AddCore(builder =>
            {
                builder.UseEntityFrameworkCore()
                    .UseDbContext<PortalDbContext>();
            })
            .AddServer(builder =>
            {
                // 仅 Token + Revocation 端点（三阶段登录走自定义授权类型，无交互式授权码流程）
                builder
                    .SetTokenEndpointUris(PortalConstants.Endpoints.Token)
                    .SetRevocationEndpointUris(PortalConstants.Endpoints.Revocation);

                // 允许的授权类型：自定义 Phase 1 身份认证 + 自定义 Phase 3 租户上下文 + 标准 refresh_token
                builder
                    .AllowCustomFlow(QlxAuthConstants.GrantTypes.PortalIdentity)
                    .AllowCustomFlow(QlxAuthConstants.GrantTypes.TenantContext)
                    .AllowCustomFlow(QlxAuthConstants.GrantTypes.TenantSilent)
                    .AllowRefreshTokenFlow();

                builder
                    .SetAccessTokenLifetime(TimeSpan.FromMinutes(auth.AccessTokenLifetimeMinutes))
                    .SetRefreshTokenLifetime(TimeSpan.FromDays(auth.RefreshTokenLifetimeDays));

                // 固定签发者（与 Validation 一致，避免依赖请求 Host 导致 aud/iss 校验漂移）
                builder.SetIssuer(new Uri(auth.Issuer));

                if (environment.IsDevelopment())
                {
                    // 开发期：临时开发证书（首次运行自动生成到用户目录）
                    builder.AddDevelopmentEncryptionCertificate()
                           .AddDevelopmentSigningCertificate();
                }
                else
                {
                    // 生产：证书来源由配置决定（PFX 文件 / 证书存储指纹），缺失时启动即失败
                    builder
                        .AddEncryptionCertificate(GetProductionCertificate(auth))
                        .AddSigningCertificate(GetProductionCertificate(auth));
                }

                builder.UseAspNetCore()
                    .EnableTokenEndpointPassthrough()
                    .DisableTransportSecurityRequirement(); // 开发期 HTTP；生产由反向代理终结 TLS
                // Revocation 端点无 Passthrough：OpenIddict 内部处理并返回标准空 200 响应
            })
            .AddValidation(builder =>
            {
                // 本地服务器集成：签发/加密密钥、iss 自动从 Server 导入，无需重复配置证书
                builder.UseLocalServer();
                builder.UseAspNetCore();

                // 显式受众（Server 侧按 principal.SetAudiences 签发，这里限定可接受范围）
                builder.SetIssuer(new Uri(auth.Issuer));
                builder.Configure(options => options.Audiences.Add(auth.Audience));
            });

        // 3. 密码哈希（PBKDF2，规格 6.3.2 BCrypt 由 Identity PasswordHasher 等价替代）+ 映射器 + 许可服务
        // 注：OpenIddict Validation 自带注册 /connect 校验 Scheme，UseAuthentication 即可激活
        services.AddSingleton<Microsoft.AspNetCore.Identity.IPasswordHasher<Domain.Identity.PortalUser>,
            Microsoft.AspNetCore.Identity.PasswordHasher<Domain.Identity.PortalUser>>();
        services.AddSingleton<IdentityMappers>();
        // 许可服务 Scoped：消费 PortalDbContext（Cache-Aside 缓存由 ICacheService 承担，DB 命中频率低）
        services.AddScoped<Qlx.Abstractions.Modules.IModuleLicenseService, Services.ModuleLicenseService>();
        // 菜单扁平查询（R-3）：租户级按许可裁剪，前端负责树组装
        services.AddScoped<Services.MenuQueryService>();
        services.AddScoped<Qlx.Abstractions.Modules.ITenantMenuQuery>(
            static sp => sp.GetRequiredService<Services.MenuQueryService>());

        return services;
    }

    /// <summary>
    /// 生产签名/加密证书加载：按 <see cref="QlxAuthOptions.ProductionCertificateOptions"/> 配置
    /// 从 PFX 文件或 Windows 证书存储加载；未配置 / 文件缺失 / 指纹未找到均启动即失败，
    /// 杜绝以开发证书上线。
    /// </summary>
    private static System.Security.Cryptography.X509Certificates.X509Certificate2 GetProductionCertificate(QlxAuthOptions auth)
    {
        var cert = auth.Certificate;
        if (string.IsNullOrWhiteSpace(cert.Source))
        {
            throw new InvalidOperationException(
                "生产环境必须配置 Qlx:Portal:Auth:Certificate（Source=File|Store），禁止以开发证书上线");
        }

        return cert.Source.Trim() switch
        {
            QlxAuthOptions.ProductionCertificateOptions.SourceFile => LoadCertificateFromFile(cert),
            QlxAuthOptions.ProductionCertificateOptions.SourceStore => LoadCertificateFromStore(cert),
            _ => throw new InvalidOperationException(
                $"不支持的 Certificate.Source：{cert.Source}（仅支持 File / Store）"),
        };
    }

    /// <summary>从 PFX 文件加载证书（Path 必填；Password 生产经部署密钥注入；net10 用 X509CertificateLoader 替代过时构造函数）。</summary>
    private static System.Security.Cryptography.X509Certificates.X509Certificate2 LoadCertificateFromFile(
        QlxAuthOptions.ProductionCertificateOptions cert)
    {
        if (string.IsNullOrWhiteSpace(cert.Path) || !File.Exists(cert.Path))
        {
            throw new InvalidOperationException($"生产证书 PFX 文件不存在：{cert.Path}");
        }

        return System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
            cert.Path,
            string.IsNullOrEmpty(cert.Password) ? null : cert.Password);
    }

    /// <summary>从 Windows 证书存储按指纹加载证书（Thumbprint 必填）。</summary>
    private static System.Security.Cryptography.X509Certificates.X509Certificate2 LoadCertificateFromStore(
        QlxAuthOptions.ProductionCertificateOptions cert)
    {
        if (string.IsNullOrWhiteSpace(cert.Thumbprint))
        {
            throw new InvalidOperationException("Certificate.Source=Store 时 Thumbprint 必填");
        }

        if (!Enum.TryParse<System.Security.Cryptography.X509Certificates.StoreLocation>(cert.StoreLocation, ignoreCase: true, out var location))
        {
            throw new InvalidOperationException($"无效的 Certificate.StoreLocation：{cert.StoreLocation}");
        }

        if (!Enum.TryParse<System.Security.Cryptography.X509Certificates.StoreName>(cert.StoreName, ignoreCase: true, out var name))
        {
            throw new InvalidOperationException($"无效的 Certificate.StoreName：{cert.StoreName}");
        }

        using var store = new System.Security.Cryptography.X509Certificates.X509Store(name, location);
        store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
        var found = store.Certificates
            .Find(System.Security.Cryptography.X509Certificates.X509FindType.FindByThumbprint, cert.Thumbprint, validOnly: false);
        return found.Count > 0
            ? found[0]
            : throw new InvalidOperationException($"证书存储 {location}/{name} 中未找到指纹为 {cert.Thumbprint} 的证书");
    }
}
