/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:35:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:35:00 +08:00 中国标准时间 星期三
 * 描述：审批服务公开接口（发起 / 审批 / 驳回 / 撤回 / 转交 / 加签 / 催办 / 查询）
 */

namespace Qlx.Abstractions.Approvals;

/// <summary>
/// 审批服务公开接口。上层模块（采购、销售、报销等）仅依赖本接口，不感知具体审批引擎实现。
/// 所有公开方法以 Async 后缀命名并接受 CancellationToken 参数。
/// </summary>
public interface IApprovalService
{
    /// <summary>发起审批。</summary>
    /// <param name="request">发起请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>审批实例 ID。</returns>
    Task<string> StartApprovalAsync(StartApprovalRequest request, CancellationToken cancellationToken);

    /// <summary>审批通过。</summary>
    /// <param name="request">通过请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ApproveAsync(ApproveRequest request, CancellationToken cancellationToken);

    /// <summary>审批驳回。</summary>
    /// <param name="request">驳回请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RejectAsync(RejectRequest request, CancellationToken cancellationToken);

    /// <summary>发起人撤回审批。</summary>
    /// <param name="request">撤回请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task WithdrawAsync(WithdrawRequest request, CancellationToken cancellationToken);

    /// <summary>加签（当前审批人在审批前增加临时审批人）。</summary>
    /// <param name="request">加签请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task AddSignerAsync(AddSignerRequest request, CancellationToken cancellationToken);

    /// <summary>转交（当前审批人将任务转给他人处理）。</summary>
    /// <param name="request">转交请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task TransferAsync(TransferRequest request, CancellationToken cancellationToken);

    /// <summary>催办（发起人或管理员催办当前审批人）。</summary>
    /// <param name="request">催办请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task UrgeAsync(UrgeRequest request, CancellationToken cancellationToken);

    /// <summary>查询指定用户的待办审批列表。</summary>
    /// <param name="userId">用户 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>待办列表。</returns>
    Task<IReadOnlyList<ApprovalTaskInfo>> GetTodoListAsync(string userId, CancellationToken cancellationToken);

    /// <summary>查询指定用户发起的审批列表。</summary>
    /// <param name="userId">用户 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已发起列表。</returns>
    Task<IReadOnlyList<ApprovalInstanceInfo>> GetMyInitiatedListAsync(string userId, CancellationToken cancellationToken);

    /// <summary>查询审批实例详情。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实例详情（未找到返回 null）。</returns>
    Task<ApprovalInstanceInfo?> GetInstanceAsync(string instanceId, CancellationToken cancellationToken);
}

/// <summary>
/// 审批流程注册接口：业务模块按自身需要注册审批定义。
/// </summary>
public interface IApprovalFlowRegistry
{
    /// <summary>注册审批定义。</summary>
    /// <param name="definitionCode">审批定义编码（全局唯一）。</param>
    /// <param name="definitionJson">审批定义 JSON。</param>
    void Register(string definitionCode, string definitionJson);

    /// <summary>获取审批定义。</summary>
    /// <param name="definitionCode">审批定义编码。</param>
    /// <returns>审批定义 JSON（未找到返回 null）。</returns>
    string? GetDefinition(string definitionCode);
}

/// <summary>
/// 审批人解析接口：根据审批定义的节点规则动态解析实际审批人。
/// </summary>
public interface IApprovalApproverResolver
{
    /// <summary>解析审批人。</summary>
    /// <param name="definitionCode">审批定义编码。</param>
    /// <param name="nodeCode">节点编码。</param>
    /// <param name="bizData">业务数据（用于条件解析）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>审批人用户 ID 列表。</returns>
    Task<IReadOnlyList<string>> ResolveAsync(
        string definitionCode,
        string nodeCode,
        IReadOnlyDictionary<string, object?> bizData,
        CancellationToken cancellationToken);
}
