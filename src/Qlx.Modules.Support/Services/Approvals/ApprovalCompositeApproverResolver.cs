/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 19:30:00 +08:00 中国标准时间 星期四
 * 描述：组合审批人解析器（2A 冻结契约 IApprovalApproverResolver 实现）——轮询各模块 IApprovalApproverSource 聚合解析
 */

using Qlx.Abstractions.Approvals;

namespace Qlx.Modules.Support.Services.Approvals;

/// <summary>
/// 组合审批人解析器：宿主 DI 聚合全部 <see cref="IApprovalApproverSource"/>（Support 兜底 + Bas 组织侧），
/// 按 <see cref="IApprovalApproverSource.CanResolve"/> 轮询路由，模块之间零引用。
/// 2A 契约 <c>ResolveAsync(definitionCode, nodeCode, bizData, ct)</c> 的节点信息经 bizData 透传键承载
/// （契约签名冻结不可改，偏差已记录）。
/// </summary>
/// <param name="sources">审批人来源集合（按注册顺序轮询）。</param>
public sealed class ApprovalCompositeApproverResolver(IEnumerable<IApprovalApproverSource> sources) : IApprovalApproverResolver
{
    /// <summary>bizData 透传键：节点审批人类型。</summary>
    public const string BizDataKeyApproverType = "approverType";

    /// <summary>bizData 透传键：节点审批人引用。</summary>
    public const string BizDataKeyApproverRef = "approverRef";

    /// <summary>bizData 透传键：发起人用户 Id。</summary>
    public const string BizDataKeyInitiatorId = "initiatorId";

    /// <summary>bizData 透传键：发起组织 Id。</summary>
    public const string BizDataKeyOrgId = "orgId";

    /// <summary>bizData 透传键：租户 Id。</summary>
    public const string BizDataKeyTenantId = "tenantId";

    /// <summary>bizData 快照载荷键（其余键视为快照字段透传给 FIELD_USER 等来源）。</summary>
    private static readonly string[] s_reservedKeys = [BizDataKeyApproverType, BizDataKeyApproverRef, BizDataKeyInitiatorId, BizDataKeyOrgId, BizDataKeyTenantId];

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ResolveAsync(
        string definitionCode,
        string nodeCode,
        IReadOnlyDictionary<string, object?> bizData,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeCode);

        var approverType = bizData.TryGetValue(BizDataKeyApproverType, out var typeValue)
            ? typeValue?.ToString() ?? string.Empty
            : string.Empty;
        var approverRef = bizData.TryGetValue(BizDataKeyApproverRef, out var refValue)
            ? refValue?.ToString()
            : null;
        var initiatorId = bizData.TryGetValue(BizDataKeyInitiatorId, out var initiatorValue)
            ? initiatorValue?.ToString() ?? string.Empty
            : string.Empty;
        var orgId = bizData.TryGetValue(BizDataKeyOrgId, out var orgValue)
            && long.TryParse(orgValue?.ToString(), out var orgParsed) ? orgParsed : 0;
        var tenantId = bizData.TryGetValue(BizDataKeyTenantId, out var tenantValue)
            && long.TryParse(tenantValue?.ToString(), out var tenantParsed) ? tenantParsed : 0;

        var snapshot = new Dictionary<string, object?>(bizData);
        foreach (var reserved in s_reservedKeys)
        {
            snapshot.Remove(reserved);
        }

        foreach (var source in sources)
        {
            if (!source.CanResolve(approverType))
            {
                continue;
            }

            var resolved = await source.ResolveAsync(
                new ApprovalApproverResolveRequest(approverType, approverRef, tenantId, initiatorId, orgId, snapshot),
                cancellationToken).ConfigureAwait(false);
            if (resolved.Count > 0)
            {
                return resolved;
            }
        }

        return [];
    }
}
