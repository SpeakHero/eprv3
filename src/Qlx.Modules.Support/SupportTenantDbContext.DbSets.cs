/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-22 23:20:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 18:50:00 +08:00 中国标准时间 星期四
 * 描述：SupportTenantDbContext 的 DbSet 集合（2B 文件域 8 张 fil_ 表 + 2C job_ + 2D msg_ + 2E apr_ 7 张）
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Modules.Support.Domain;

namespace Qlx.Modules.Support;

public sealed partial class SupportTenantDbContext
{
    /// <summary>存储对象（fil_storage_objects：目录 + 文件主档）。</summary>
    public DbSet<StorageObject> StorageObjects => Set<StorageObject>();

    /// <summary>文件版本（fil_file_versions）。</summary>
    public DbSet<FileVersion> FileVersions => Set<FileVersion>();

    /// <summary>ACL 条目（fil_acl_entries）。</summary>
    public DbSet<FileAclEntry> FileAclEntries => Set<FileAclEntry>();

    /// <summary>ACL 模板（fil_acl_templates）。</summary>
    public DbSet<AclTemplate> AclTemplates => Set<AclTemplate>();

    /// <summary>归类规则（fil_classify_rules）。</summary>
    public DbSet<ClassifyRule> ClassifyRules => Set<ClassifyRule>();

    /// <summary>临时文件（fil_temp_files）。</summary>
    public DbSet<TempFile> TempFiles => Set<TempFile>();

    /// <summary>分享链接（fil_share_links）。</summary>
    public DbSet<ShareLink> ShareLinks => Set<ShareLink>();

    /// <summary>操作日志（fil_op_logs）。</summary>
    public DbSet<FileOpLog> FileOpLogs => Set<FileOpLog>();

    // ============ 后台任务中心（2C，job_ 前缀） ============

    /// <summary>任务定义（job_definitions）。</summary>
    public DbSet<JobDefinitionEntity> JobDefinitions => Set<JobDefinitionEntity>();

    /// <summary>租户级任务配置（job_configs）。</summary>
    public DbSet<JobConfigEntity> JobConfigs => Set<JobConfigEntity>();

    /// <summary>任务执行历史（job_executions）。</summary>
    public DbSet<JobExecutionEntity> JobExecutions => Set<JobExecutionEntity>();

    /// <summary>任务执行日志（job_logs，只增不改）。</summary>
    public DbSet<JobLogEntity> JobLogs => Set<JobLogEntity>();

    // ============ 信息中心（2D，msg_ 前缀） ============

    /// <summary>消息主档（msg_messages）。</summary>
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();

    /// <summary>收件箱（msg_message_inboxes）。</summary>
    public DbSet<MessageInboxEntity> MessageInboxes => Set<MessageInboxEntity>();

    /// <summary>消息模板（msg_message_templates）。</summary>
    public DbSet<MessageTemplateEntity> MessageTemplates => Set<MessageTemplateEntity>();

    /// <summary>订阅配置（msg_message_subscriptions）。</summary>
    public DbSet<MessageSubscriptionEntity> MessageSubscriptions => Set<MessageSubscriptionEntity>();

    /// <summary>渠道发送日志（msg_send_logs，只增不改）。</summary>
    public DbSet<MessageSendLogEntity> MessageSendLogs => Set<MessageSendLogEntity>();

    /// <summary>全局项状态叠加（msg_global_state，正文不复制）。</summary>
    public DbSet<MessageGlobalStateEntity> MessageGlobalStates => Set<MessageGlobalStateEntity>();

    // ============ 审批中心（2E，apr_ 前缀） ============
    // DbSet 属性名即表名后缀来源（基类取 snake_case(DbSet 名) 拼前缀），须与 apr_ 表名约定对齐

    /// <summary>流程定义（apr_flow_definitions）。</summary>
    public DbSet<ApprovalFlowDefinition> FlowDefinitions => Set<ApprovalFlowDefinition>();

    /// <summary>流程节点（apr_flow_nodes，定义态）。</summary>
    public DbSet<ApprovalFlowNode> FlowNodes => Set<ApprovalFlowNode>();

    /// <summary>条件分支（apr_flow_branches，定义态；expr_json 为条件引擎 AST JSON）。</summary>
    public DbSet<ApprovalFlowBranch> FlowBranches => Set<ApprovalFlowBranch>();

    /// <summary>审批实例（apr_instances）。</summary>
    public DbSet<ApprovalInstance> Instances => Set<ApprovalInstance>();

    /// <summary>节点任务（apr_tasks）。</summary>
    public DbSet<ApprovalTask> Tasks => Set<ApprovalTask>();

    /// <summary>操作日志（apr_op_logs，只增不改）。</summary>
    public DbSet<ApprovalLog> OpLogs => Set<ApprovalLog>();

    /// <summary>委托（apr_delegates）。</summary>
    public DbSet<ApprovalDelegation> Delegates => Set<ApprovalDelegation>();

    // ============ 打印中心（2F，prt_ 前缀） ============

    /// <summary>打印模板（prt_print_templates）。</summary>
    public DbSet<PrintTemplate> PrintTemplates => Set<PrintTemplate>();
}
