/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 23:50:00 +08:00 中国标准时间 星期日
 * 描述：Bas 编码规则 DTO——仅扁平原始字段与数值枚举（B-2），分段模板原样下发由前端只读展示
 */

namespace Qlx.Modules.Bas.Dtos;

/// <summary>
/// 编码规则扁平行（原始字段）。规则本身不可修改，仅可启停（N-1：编码统一由后端发号器生成）。
/// </summary>
/// <param name="BizType">业务类型（org / supplier / customer / employee / labour / category）。</param>
/// <param name="Prefix">编码前缀（如 SUP-）。</param>
/// <param name="SegmentsJson">分段模板 JSON 数组（只读展示）。</param>
/// <param name="Status">启停状态（1=启用 / 2=停用）。</param>
/// <param name="UpdatedAt">更新时间。</param>
public sealed record CodeRuleRow(
    string BizType,
    string Prefix,
    string SegmentsJson,
    int Status,
    DateTimeOffset UpdatedAt);
