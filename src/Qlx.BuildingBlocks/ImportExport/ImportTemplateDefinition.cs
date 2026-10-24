/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 12:00:00 +08:00 中国标准时间 星期五
 * 描述：导入导出模板扩展元数据定义（BuildingBlocks 内部承载，2A 冻结契约之外的能力扩展）
 */

namespace Qlx.BuildingBlocks.ImportExport;

/// <summary>
/// 导入列扩展元数据。在 2A 冻结契约 <c>ImportTemplateColumn</c>（列名/显示名/必填/类型/说明）基础上
/// 追加长度 / 字典编码 / 示例值三项生成与校验所需元数据，禁止改动冻结契约。
/// </summary>
/// <param name="ColumnName">字段名（导入结果映射键，进程内稳定标识）。</param>
/// <param name="DisplayName">Excel 表头显示名（模板生成与表头匹配依据）。</param>
/// <param name="IsRequired">是否必填。</param>
/// <param name="DataType">数据类型（<see cref="ImportColumnDataTypes"/>，缺省 string）。</param>
/// <param name="MaxLength">最大长度（仅 string 类型生效，null 不限）。</param>
/// <param name="DictionaryCode">字典编码（非空时按字典校验取值合法性）。</param>
/// <param name="ExampleValue">示例值（模板「数据」页首行示例）。</param>
/// <param name="Description">说明（模板「说明」页展示）。</param>
public sealed record ImportColumnDefinition(
    string ColumnName,
    string DisplayName,
    bool IsRequired,
    string? DataType = null,
    int? MaxLength = null,
    string? DictionaryCode = null,
    string? ExampleValue = null,
    string? Description = null);

/// <summary>
/// 导入模板扩展定义（含完整列元数据）。
/// </summary>
/// <param name="TemplateCode">模板编码（全局唯一）。</param>
/// <param name="Name">模板名称。</param>
/// <param name="ModuleCode">归属模块编码。</param>
/// <param name="Columns">列定义（顺序即模板列顺序）。</param>
public sealed record ImportTemplateDefinition(
    string TemplateCode,
    string Name,
    string ModuleCode,
    IReadOnlyList<ImportColumnDefinition> Columns);

/// <summary>
/// 支持的导入列数据类型常量（归口定义，禁止散落硬编码）。
/// </summary>
public static class ImportColumnDataTypes
{
    /// <summary>字符串（缺省类型）。</summary>
    public const string Text = "string";

    /// <summary>数值（decimal 兼容）。</summary>
    public const string Number = "number";

    /// <summary>日期时间。</summary>
    public const string Date = "date";

    /// <summary>布尔（true/false，中文 是/否 兼容）。</summary>
    public const string Boolean = "boolean";
}
