/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 描述：Support 模块端点授权规则基线——原控制器硬编码 [Authorize] 标注的 1:1 快照
 *       （5 个业务控制器类级租户上下文 + 45 条动作级权限码；Message 自助端点无动作级权限码，
 *       仅继承控制器级；MessageStream 仅认证）。
 *       规则调整优先改 config/common/authorization.json 覆盖，勿改本基线。
 */

using Qlx.Abstractions.Authorization;

namespace Qlx.Modules.Support;

/// <summary>Support 模块端点授权规则映射提供方。</summary>
public sealed class SupportEndpointPolicyMap : IEndpointPolicyMapProvider
{
    /// <inheritdoc />
    public IReadOnlyList<EndpointPolicyRule> GetRules() =>
    [
        // ======= 文件中心（类级 QlxTenantContext）=======
        new() { Controller = "File", RequireTenantContext = true },
        new() { Controller = "File", Action = "Upload", PermissionCode = "support.file.upload" },
        new() { Controller = "File", Action = "GetMetadata", PermissionCode = "support.file.download" },
        new() { Controller = "File", Action = "List", PermissionCode = "support.file.menu" },
        new() { Controller = "File", Action = "Download", PermissionCode = "support.file.download" },
        new() { Controller = "File", Action = "Preview", PermissionCode = "support.file.download" },
        new() { Controller = "File", Action = "RecycleList", PermissionCode = "support.file.menu" },
        new() { Controller = "File", Action = "Delete", PermissionCode = "support.file.delete" },
        new() { Controller = "File", Action = "Restore", PermissionCode = "support.file.restore" },
        new() { Controller = "File", Action = "Purge", PermissionCode = "support.file.delete" },
        new() { Controller = "File", Action = "ListAcl", PermissionCode = "support.file.acl" },
        new() { Controller = "File", Action = "GrantAcl", PermissionCode = "support.file.acl" },
        new() { Controller = "File", Action = "RevokeAcl", PermissionCode = "support.file.acl" },

        // ======= 后台任务中心（类级 QlxTenantContext）=======
        new() { Controller = "Job", RequireTenantContext = true },
        new() { Controller = "Job", Action = "Trigger", PermissionCode = "support.job.trigger" },
        new() { Controller = "Job", Action = "Cancel", PermissionCode = "support.job.cancel" },
        new() { Controller = "Job", Action = "GetStatus", PermissionCode = "support.job.view" },
        new() { Controller = "Job", Action = "History", PermissionCode = "support.job.view" },
        new() { Controller = "Job", Action = "Logs", PermissionCode = "support.job.view" },
        new() { Controller = "Job", Action = "Definitions", PermissionCode = "support.job.view" },
        new() { Controller = "Job", Action = "ToggleDefinition", PermissionCode = "support.job.config" },

        // ======= 信息中心（类级 QlxTenantContext；自助端点仅继承类级，无动作级权限码）=======
        new() { Controller = "Message", RequireTenantContext = true },
        new() { Controller = "Message", Action = "Inbox", PermissionCode = "support.message.read" },
        new() { Controller = "Message", Action = "Sent", PermissionCode = "support.message.read" },
        // UnreadCount / MarkRead / MarkAllRead / DeleteInboxItem / MarkGlobalItem / Subscriptions：自助端点
        new() { Controller = "Message", Action = "SendLogs", PermissionCode = "support.message.read" },
        new() { Controller = "Message", Action = "RetryFailed", PermissionCode = "support.message.send" },
        new() { Controller = "Message", Action = "ToggleSubscription", PermissionCode = "support.message.subscription" },

        // ======= 审批中心（类级 QlxTenantContext）=======
        new() { Controller = "Approval", RequireTenantContext = true },
        new() { Controller = "Approval", Action = "Start", PermissionCode = "support.approval.initiate" },
        new() { Controller = "Approval", Action = "Approve", PermissionCode = "support.approval.todo" },
        new() { Controller = "Approval", Action = "Reject", PermissionCode = "support.approval.todo" },
        new() { Controller = "Approval", Action = "Withdraw", PermissionCode = "support.approval.initiate" },
        new() { Controller = "Approval", Action = "Urge", PermissionCode = "support.approval.todo" },
        new() { Controller = "Approval", Action = "AddSign", PermissionCode = "support.approval.todo" },
        new() { Controller = "Approval", Action = "Transfer", PermissionCode = "support.approval.todo" },
        new() { Controller = "Approval", Action = "MyTodos", PermissionCode = "support.approval.todo" },
        new() { Controller = "Approval", Action = "MyDone", PermissionCode = "support.approval.todo" },
        new() { Controller = "Approval", Action = "MyInitiated", PermissionCode = "support.approval.initiate" },
        new() { Controller = "Approval", Action = "Instance", PermissionCode = "support.approval.instance" },
        new() { Controller = "Approval", Action = "Flows", PermissionCode = "support.approval.flow" },
        new() { Controller = "Approval", Action = "Delegates", PermissionCode = "support.approval.delegate" },
        new() { Controller = "Approval", Action = "CreateDelegate", PermissionCode = "support.approval.delegate" },
        new() { Controller = "Approval", Action = "RevokeDelegate", PermissionCode = "support.approval.delegate" },

        // ======= 打印中心（类级 QlxTenantContext）=======
        new() { Controller = "Print", RequireTenantContext = true },
        new() { Controller = "Print", Action = "List", PermissionCode = "support.print.template" },
        new() { Controller = "Print", Action = "GetByCode", PermissionCode = "support.print.template" },
        new() { Controller = "Print", Action = "Create", PermissionCode = "support.print.template" },
        new() { Controller = "Print", Action = "Update", PermissionCode = "support.print.template" },
        new() { Controller = "Print", Action = "SetStatus", PermissionCode = "support.print.template" },
        new() { Controller = "Print", Action = "Render", PermissionCode = "support.print.render" },

        // ======= 消息实时流（SSE，仅认证）=======
        new() { Controller = "MessageStream" },
    ];
}
