/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 00:20:00 +08:00 中国标准时间 星期一
 * 描述：编码规则服务契约——规则扁平查询 + 启停（规则内容不可修改，编码统一由后端发号器生成）
 */

using Qlx.Modules.Bas.Dtos;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 编码规则服务。规则仅可查询与启停，编码一经统一发号器生成即不可由前端传入或改写（N-1）。
/// </summary>
public interface ICodeRuleService
{
    /// <summary>编码规则扁平列表（业务类型 / 启停状态筛选，按业务类型排序）。</summary>
    Task<IReadOnlyList<CodeRuleRow>> ListAsync(string? bizType, int? status, CancellationToken ct);

    /// <summary>启用指定业务类型的编码规则。</summary>
    Task<CodeRuleRow> EnableAsync(string bizType, CancellationToken ct);

    /// <summary>停用指定业务类型的编码规则。</summary>
    Task<CodeRuleRow> DisableAsync(string bizType, CancellationToken ct);
}
