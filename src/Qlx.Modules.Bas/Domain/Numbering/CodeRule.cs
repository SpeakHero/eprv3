/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 12:00:00 +08:00 中国标准时间 星期日
 * 描述：主数据编码规则实体（bas_code_rules）——仅存分段模板，不存流水值
 */

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain.Numbering;

/// <summary>
/// 主数据编码规则（表 bas_code_rules）。租户内按业务类型唯一。
/// <para>编码 = Prefix + 按 SegmentsJson 顺序渲染的段。段支持 <c>{type}</c>（类型代码）、
/// <c>{parent}</c>（上级编码）、<c>{yyyyMM}</c>（UTC 年月）、<c>{seq:N}</c>（流水号左补零 N 位）与固定文本。</para>
/// </summary>
[Index(nameof(TenantId), nameof(BizType), IsUnique = true)]
public sealed class CodeRule : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>业务类型（org / supplier / customer / employee / labour / category）。</summary>
    [MaxLength(32)]
    public string BizType { get; internal set; } = string.Empty;

    /// <summary>编码前缀（如 ORG- / SUP-）。</summary>
    [MaxLength(16)]
    public string Prefix { get; internal set; } = string.Empty;

    /// <summary>分段模板 JSON 数组（如 ["{type}","-","{seq:2}"]）。</summary>
    [MaxLength(200)]
    public string SegmentsJson { get; internal set; } = "[]";

    /// <summary>规则启停状态。</summary>
    public Domain.EntityStatus Status { get; private set; } = Domain.EntityStatus.Enabled;

    public CodeRule() { }

    public CodeRule(long tenantId, string bizType, string prefix, string segmentsJson)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bizType);
        TenantId = tenantId;
        BizType = bizType;
        Prefix = prefix ?? string.Empty;
        SegmentsJson = segmentsJson;
    }

    /// <summary>停用规则。</summary>
    public void Disable() => Status = Domain.EntityStatus.Disabled;

    /// <summary>启用规则。</summary>
    public void Enable() => Status = Domain.EntityStatus.Enabled;
}
