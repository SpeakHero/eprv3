/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 12:00:00 +08:00 中国标准时间 星期日
 * 描述：Bas 主数据编码发号器实现——规则懒种子 + 分段模板渲染 + 流水原子取号 + 冲突重试
 */

using System.Globalization;
using System.Text.Json;

using Qlx.Abstractions.Exceptions;
using Qlx.BuildingBlocks.Numbering;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Domain.Numbering;
using Qlx.Modules.Bas.Repositories;

namespace Qlx.Modules.Bas.Services.Numbering;

/// <summary>
/// Bas 主数据编码发号器。
/// <para>规则来源：租户 <c>bas_code_rules</c>；本租户该 bizType 无规则时按内置默认模板懒种子写入一次。</para>
/// <para>段语义：<c>{type}</c> 类型代码、<c>{parent}</c> 上级编码、<c>{yyyyMM}</c> UTC 年月、
/// <c>{seq:N}</c> 流水号左补零 N 位；其余文本原样输出。</para>
/// </summary>
/// <param name="repo">Bas 租户仓储（规则读取 / 懒种子）。</param>
/// <param name="serialSequenceStore">单据序号存储（原子递增）。</param>
internal sealed class BasCodeNumberGenerator(
    IBasTenantRepository repo,
    ISerialSequenceStore serialSequenceStore) : IBasCodeNumberGenerator
{
    /// <summary>冲突重试上限（与 N-3-3 一致）。</summary>
    private const int MaxRetries = 100;

    private readonly IBasTenantRepository _repo = repo;
    private readonly ISerialSequenceStore _serialSequenceStore = serialSequenceStore;

    /// <inheritdoc />
    public async Task<string> NextAsync(
        string bizType,
        long tenantId,
        string? parentCode,
        string? typeCode,
        Func<string, CancellationToken, Task<bool>> isConflict,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bizType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentNullException.ThrowIfNull(isConflict);

        CodeRule rule = await ResolveRuleAsync(bizType, tenantId, ct).ConfigureAwait(false);

        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            string candidate = await RenderAsync(rule, bizType, tenantId, parentCode, typeCode, ct).ConfigureAwait(false);
            if (!await isConflict(candidate, ct).ConfigureAwait(false))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"业务编码生成冲突重试已达上限（{MaxRetries} 次）：bizType={bizType}，tenantId={tenantId}。");
    }

    /// <summary>读取本租户规则；缺失时按内置默认模板懒种子。</summary>
    private async Task<CodeRule> ResolveRuleAsync(string bizType, long tenantId, CancellationToken ct)
    {
        CodeRule? rule = await _repo.FindCodeRuleAsync(bizType, ct).ConfigureAwait(false);
        if (rule is not null)
        {
            return rule;
        }

        if (!BasCodeRuleDefaults.TryGet(bizType, out string prefix, out string segmentsJson))
        {
            throw new InvalidOperationException($"未配置业务编码规则：bizType={bizType}");
        }

        // 默认模板语法自检：配置错误按 422 校验异常返回，禁止落库后才在渲染期失败
        if (!CodeRuleTemplate.TryParse(segmentsJson, out _, out string? error))
        {
            throw new ValidationException("segmentsJson", error ?? "编码规则分段模板非法");
        }

        rule = new CodeRule(tenantId, bizType, prefix, segmentsJson);
        await _repo.AddCodeRuleAsync(rule, ct).ConfigureAwait(false);
        _ = await _repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return rule;
    }

    /// <summary>渲染一次候选编码（含流水取号）。</summary>
    private async Task<string> RenderAsync(
        CodeRule rule, string bizType, long tenantId, string? parentCode, string? typeCode, CancellationToken ct)
    {
        if (!CodeRuleTemplate.TryParse(rule.SegmentsJson, out string[] segments, out string? error))
        {
            throw new ValidationException("segmentsJson", error ?? "编码规则分段模板非法");
        }

        string sequenceKey = $"bas-code:{bizType}:{tenantId}:{parentCode ?? "-"}";
        var builder = new System.Text.StringBuilder(rule.Prefix);

        foreach (string segment in segments)
        {
            if (segment == CodeRuleTemplate.TypeToken)
            {
                _ = builder.Append(typeCode ?? string.Empty);
            }
            else if (segment == CodeRuleTemplate.ParentToken)
            {
                _ = builder.Append(parentCode ?? string.Empty);
            }
            else if (segment == CodeRuleTemplate.YearMonthToken)
            {
                _ = builder.Append(DateTime.UtcNow.ToString("yyyyMM", CultureInfo.InvariantCulture));
            }
            else if (CodeRuleTemplate.TryParseSequenceDigits(segment, out int digits))
            {
                long next = await _serialSequenceStore.GetNextAsync(sequenceKey, ct).ConfigureAwait(false);
                _ = builder.Append(next.ToString("D" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
            }
            else
            {
                _ = builder.Append(segment);
            }
        }

        return builder.ToString();
    }
}

/// <summary>
/// 内置默认编码规则（租户规则缺失时懒种子写入）与组织类型代码映射。
/// </summary>
internal static class BasCodeRuleDefaults
{
    /// <summary>组织类型 → 编码类型段代码（D2：集团 GS / 子公司 SUB / 部门 BM / 项目部 XM）。</summary>
    /// <param name="type">组织节点类型。</param>
    /// <returns>类型代码。</returns>
    public static string GetOrgTypeCode(OrgNodeType type) => type switch
    {
        OrgNodeType.Group => "GS",
        OrgNodeType.Subsidiary => "SUB",
        OrgNodeType.Department => "BM",
        OrgNodeType.Project => "XM",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未知的组织节点类型"),
    };

    /// <summary>按业务类型取默认前缀与分段模板。</summary>
    /// <param name="bizType">业务类型。</param>
    /// <param name="prefix">前缀。</param>
    /// <param name="segmentsJson">分段模板 JSON。</param>
    /// <returns>存在默认规则返回 <see langword="true"/>。</returns>
    public static bool TryGet(string bizType, out string prefix, out string segmentsJson)
    {
        (prefix, segmentsJson) = bizType switch
        {
            "org" => ("ORG-", """["{type}","-","{seq:2}"]"""),
            "supplier" => ("SUP-", """["{seq:6}"]"""),
            "customer" => ("CUS-", """["{seq:6}"]"""),
            "employee" => ("EMP-", """["{seq:6}"]"""),
            "labour" => ("LAB-", """["{seq:6}"]"""),
            "category" => ("", """["{parent}","{seq:2}"]"""),
            _ => (string.Empty, string.Empty),
        };

        return !string.IsNullOrEmpty(segmentsJson);
    }
}
