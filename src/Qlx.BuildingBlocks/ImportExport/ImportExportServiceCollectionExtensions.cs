/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 12:00:00 +08:00 中国标准时间 星期五
 * 描述：导入导出能力 DI 装配扩展——IImportExportService（ClosedXML 实现）+ 空模板注册表兜底
 */

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Qlx.Abstractions.ImportExport;

namespace Qlx.BuildingBlocks.ImportExport;

/// <summary>
/// 导入导出能力装配扩展。模板注册表（<see cref="IImportTemplateRegistry"/>）、
/// 导入行落点（<see cref="IImportRowSink"/>）、导出数据源（<see cref="IExportDataProvider"/>）、
/// 字典校验器（<see cref="IImportDictionaryValidator"/>）由业务模块自行注册，本扩展只装配公共实现。
/// </summary>
public static class ImportExportServiceCollectionExtensions
{
    /// <summary>
    /// 注册导入导出公共能力（<see cref="ImportExportService"/>，Scoped——多租户场景服务一律 Scoped）。
    /// 未注册业务注册表时以 <see cref="EmptyImportTemplateRegistry"/> 兜底（模板查找返回 null，
    /// 导入 / 导出 / 模板查询统一得到「未找到」语义而非 DI 构造失败）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddQlxImportExport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IImportExportService, ImportExportService>();
        services.TryAddSingleton<IImportTemplateRegistry, EmptyImportTemplateRegistry>();
        return services;
    }
}

/// <summary>
/// 空导入模板注册表（默认兜底实现）：不持有任何模板定义，宿主 / 业务模块注册自己的
/// <see cref="IImportTemplateRegistry"/> 后整体替换。
/// </summary>
public sealed class EmptyImportTemplateRegistry : IImportTemplateRegistry
{
    /// <inheritdoc />
    public ImportTemplateDefinition? Find(string templateCode) => null;
}
