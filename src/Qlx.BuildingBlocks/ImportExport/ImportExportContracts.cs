/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 12:00:00 +08:00 中国标准时间 星期五
 * 描述：导入导出支撑契约——模板注册表 / 导入落点 / 导出数据源 / 字典批量校验器
 */

namespace Qlx.BuildingBlocks.ImportExport;

/// <summary>
/// 导入模板注册表契约。模板定义为代码态声明（各业务模块启动期注册），
/// <see cref="ImportExportService"/> 依此完成模板生成、表头校验与导出投影。
/// </summary>
public interface IImportTemplateRegistry
{
    /// <summary>
    /// 按模板编码查找模板定义。
    /// </summary>
    /// <param name="templateCode">模板编码。</param>
    /// <returns>模板定义（未找到返回 null）。</returns>
    ImportTemplateDefinition? Find(string templateCode);
}

/// <summary>
/// 导入行落点契约：经校验通过的行由对应模板的落点实现批量写入业务存储。
/// 每个 <c>IImportRowSink</c> 实例归属一个模板编码（<see cref="TemplateCode"/>）。
/// </summary>
public interface IImportRowSink
{
    /// <summary>归属的导入模板编码。</summary>
    string TemplateCode { get; }

    /// <summary>
    /// 批量写入校验通过的导入行。实现必须批量读写（禁止逐行往返，规范 §8.15）。
    /// </summary>
    /// <param name="rows">校验通过的行集合（键 = 列字段名 <see cref="ImportColumnDefinition.ColumnName"/>）。</param>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际成功写入的行数。</returns>
    Task<int> WriteRowsAsync(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        Guid tenantId,
        CancellationToken cancellationToken);
}

/// <summary>
/// 导出数据源契约：按模板编码提供投影数据行。每个实例归属一个模板编码。
/// </summary>
public interface IExportDataProvider
{
    /// <summary>归属的导出模板编码。</summary>
    string TemplateCode { get; }

    /// <summary>
    /// 按导出条件批量拉取数据行（键 = 列字段名 <see cref="ImportColumnDefinition.ColumnName"/>）。
    /// </summary>
    /// <param name="criteria">导出条件（业务模块自定义键值）。</param>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>数据行集合。</returns>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(
        IReadOnlyDictionary<string, object?> criteria,
        Guid tenantId,
        CancellationToken cancellationToken);
}

/// <summary>
/// 导入字典批量校验器契约：校验字典编码列取值是否属于合法字典项。
/// <see cref="ImportExportService"/> 按「字典编码 → 待校验值集合」分批调用，
/// 实现方必须一次批量返回（禁止逐值往返）。
/// </summary>
public interface IImportDictionaryValidator
{
    /// <summary>
    /// 批量校验同一字典编码下的候选值。
    /// </summary>
    /// <param name="dictionaryCode">字典编码。</param>
    /// <param name="values">候选值集合（去重后）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>值 → 是否合法映射（未出现的键视为非法）。</returns>
    Task<IReadOnlyDictionary<string, bool>> ValidateAsync(
        string dictionaryCode,
        IReadOnlyCollection<string> values,
        CancellationToken cancellationToken);
}
