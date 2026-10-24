/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 描述：ASP.NET Core Data Protection DI 装配扩展，密钥环目录与生命周期配置化
 */

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Qlx.BuildingBlocks.Security;

/// <summary>
/// Data Protection 装配扩展。承载密钥引用解密、敏感数据加密的统一底座，
/// 密钥只存引用、密文经本组件加解密的约定由本装配落地。
/// </summary>
public static class DataProtectionServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Data Protection。配置节 <c>Qlx:DataProtection</c>；
    /// <c>KeyDirectory</c> 非空时将密钥环持久化到该目录并设定密钥有效期。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置根。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddQlxDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DataProtectionOptions>()
            .Bind(configuration.GetSection(DataProtectionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = configuration.GetSection(DataProtectionOptions.SectionName).Get<DataProtectionOptions>() ?? new DataProtectionOptions();

        var builder = services.AddDataProtection()
            .SetApplicationName(options.ApplicationName)
            .SetDefaultKeyLifetime(TimeSpan.FromDays(options.KeyLifetimeDays));

        if (!string.IsNullOrWhiteSpace(options.KeyDirectory))
        {
            builder.PersistKeysToFileSystem(new DirectoryInfo(options.KeyDirectory));
        }

        return services;
    }
}
