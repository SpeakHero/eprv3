/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 11:00:00 +08:00 中国标准时间 星期日
 * 描述：导入模板定义提供方契约——各业务模块按需实现并注册，由组合注册表遍历汇总
 */

namespace Qlx.BuildingBlocks.ImportExport;

/// <summary>
/// 导入模板定义提供方契约。业务模块实现本接口声明本模块全部模板定义，
/// 由 <see cref="CompositeImportTemplateRegistry"/> 遍历汇总（多模块模板并存）。
/// </summary>
public interface IImportTemplateDefinitionProvider
{
    /// <summary>
    /// 获取本提供方声明的全部模板定义（模板编码全局唯一）。
    /// </summary>
    /// <returns>模板定义列表（无模板返回空列表）。</returns>
    IReadOnlyList<ImportTemplateDefinition> GetDefinitions();
}
