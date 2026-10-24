/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 描述：单据编号体系 DI 装配扩展
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Qlx.BuildingBlocks.Numbering.Pinyin;

namespace Qlx.BuildingBlocks.Numbering;

/// <summary>
/// 编号体系装配扩展：单据编号生成器 + 通用业务编码生成器 + 序号存储 + 拼音转换。
/// </summary>
public static class NumberingServiceCollectionExtensions
{
    /// <summary>
    /// 注册单据编号生成器、通用业务编码生成器、序号存储与拼音转换器。配置节 <c>Qlx:Numbering</c>。
    /// 默认注册内存序号存储；生产环境在调用本方法后以后备持久化实现覆盖
    /// <see cref="ISerialSequenceStore"/> 注册即可。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置根。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddQlxNumbering(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DocumentNumberingOptions>()
            .Bind(configuration.GetSection(DocumentNumberingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<ISerialSequenceStore, InMemorySerialSequenceStore>();
        // 生成器依赖 ITenantContext（请求级），故为 Scoped：序号键带租户段，禁止跨租户共用流水
        services.AddScoped<IDocumentNumberGenerator, DocumentNumberGenerator>();

        // 通用业务编码生成器：拼音转换无状态为 Singleton；编码生成器依赖序号存储为 Singleton
        services.TryAddSingleton<IPinyinConverter, PinyinConverter>();
        services.AddSingleton<IEncodingGenerator, EncodingGenerator>();

        return services;
    }
}
