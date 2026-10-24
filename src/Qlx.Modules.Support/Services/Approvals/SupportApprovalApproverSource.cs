/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 19:20:00 +08:00 中国标准时间 星期四
 * 描述：Support 兜底审批人来源（INITIATOR / USER / FIELD_USER），组织侧类型由 Bas 来源实现
 */

using System.Text.Json;
using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Serialization;

namespace Qlx.Modules.Support.Services.Approvals;

/// <summary>
/// Support 兜底审批人来源：解析 INITIATOR（发起人）、USER（指定人）、FIELD_USER（快照字段人员）。
/// ROLE / POSITION / DEPT_LEADER 由 Bas 模块来源承担（宿主 DI 聚合，模块间零引用）。
/// </summary>
public sealed class SupportApprovalApproverSource : IApprovalApproverSource
{
    /// <summary>本来源支持的全部审批人类型。</summary>
    private static readonly string[] s_supportedTypes =
        [ApprovalApproverTypes.Initiator, ApprovalApproverTypes.User, ApprovalApproverTypes.FieldUser];

    /// <inheritdoc />
    public bool CanResolve(string approverType) => s_supportedTypes.Contains(approverType);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ResolveAsync(
        ApprovalApproverResolveRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> resolved = request.ApproverType switch
        {
            ApprovalApproverTypes.Initiator => ResolveInitiator(request),
            ApprovalApproverTypes.User => SplitUserRefs(request.ApproverRef),
            ApprovalApproverTypes.FieldUser => ResolveFieldUser(request),
            _ => [],
        };
        return Task.FromResult(resolved);
    }

    /// <summary>解析发起人（必填；缺失视为定义配置错误）。</summary>
    /// <param name="request">解析请求。</param>
    /// <returns>发起人用户 Id 列表。</returns>
    private static IReadOnlyList<string> ResolveInitiator(ApprovalApproverResolveRequest request)
        => string.IsNullOrWhiteSpace(request.InitiatorUserId) ? [] : [request.InitiatorUserId];

    /// <summary>解析指定人引用（逗号分隔用户 Id 列表，去空保序）。</summary>
    /// <param name="approverRef">审批人引用。</param>
    /// <returns>用户 Id 列表。</returns>
    private static IReadOnlyList<string> SplitUserRefs(string? approverRef)
        => string.IsNullOrWhiteSpace(approverRef)
            ? []
            : [.. approverRef.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>
    /// 解析单据字段人员：approver_ref 为快照字段名，字段值支持单用户 Id、逗号分隔列表或 JSON 字符串数组。
    /// </summary>
    /// <param name="request">解析请求。</param>
    /// <returns>用户 Id 列表（字段缺失或为空返回空列表）。</returns>
    private static IReadOnlyList<string> ResolveFieldUser(ApprovalApproverResolveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ApproverRef)
            || !request.Snapshot.TryGetValue(request.ApproverRef, out var raw)
            || raw is null)
        {
            return [];
        }

        // 单字符串快照值直接走标量路径（最常见的单审批人形态）
        if (raw is string single)
        {
            return string.IsNullOrWhiteSpace(single) ? [] : [single.Trim()];
        }

        // 复合值统一序列化后按 JSON 数组解析；非数组形态回退标量，不因形态差异抛异常
        var json = JsonSerializer.Serialize(raw, QlxJsonDefaults.Default);
        try
        {
            if (JsonSerializer.Deserialize<string[]>(json, QlxJsonDefaults.Default) is { Length: > 0 } list)
            {
                return [.. list.Where(static id => !string.IsNullOrWhiteSpace(id))];
            }
        }
        catch (JsonException)
        {
            // 非 JSON 数组形态（如数字/布尔），回退标量解析
        }

        var scalar = raw.ToString();
        return string.IsNullOrWhiteSpace(scalar) ? [] : [scalar.Trim()];
    }
}
