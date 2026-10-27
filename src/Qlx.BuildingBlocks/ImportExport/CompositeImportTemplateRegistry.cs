/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 11:00:00 +08:00 中国标准时间 星期日
 * 描述：组合导入模板注册表——遍历全部 IImportTemplateDefinitionProvider 汇总模板定义（多模块并存）
 */

namespace Qlx.BuildingBlocks.ImportExport;

/// <summary>
/// 组合导入模板注册表：按注册顺序遍历 <see cref="IImportTemplateDefinitionProvider"/> 查找模板定义。
/// 各业务模块各自注册 provider；无 provider 时查找返回 null（与空实现兜底语义一致）。
/// </summary>
/// <param name="providers">模板定义提供方集合（DI 注入，允许为空）。</param>
public sealed class CompositeImportTemplateRegistry(IEnumerable<IImportTemplateDefinitionProvider> providers)
    : IImportTemplateRegistry
{
    private readonly IReadOnlyList<IImportTemplateDefinitionProvider> _providers = [.. providers];

    /// <inheritdoc />
    public ImportTemplateDefinition? Find(string templateCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateCode);

        foreach (var provider in _providers)
        {
            foreach (var definition in provider.GetDefinitions())
            {
                if (string.Equals(definition.TemplateCode, templateCode, StringComparison.Ordinal))
                {
                    return definition;
                }
            }
        }

        return null;
    }
}
