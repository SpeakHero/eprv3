/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 00:35:00 +08:00 中国标准时间 星期五
 * 描述：租户种子服务（幂等）——文件域：7 业务域目录骨架 + 默认 ACL 模板 + 默认归类规则；
 *       信息中心：8 类消息模板（InApp 渠道，编码即幂等键）；
 *       审批中心：4 个默认审批流程（条件分支 expr_json 经条件引擎 Parse 产出）
 */

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Expressions;
using Qlx.Abstractions.Messages;
using Qlx.Abstractions.Serialization;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Jobs.Files;

namespace Qlx.Modules.Support.Services.Files;

/// <summary>
/// 租户种子服务（幂等）。文件域：目录骨架 + 默认 ACL 模板 + 默认归类规则；
/// 信息中心：8 类消息模板（InApp 渠道各一份，write-through 兜底渠道）；
/// 审批中心：4 个默认流程（采购申请 / 费用报销 / 合同评审 / 劳务工资表）。
/// </summary>
public sealed partial class SupportSeedService(
    SupportTenantDbContext db,
    IConditionExpressionEngine expressionEngine,
    ILogger<SupportSeedService> logger)
{
    private readonly SupportTenantDbContext _db = db;
    private readonly IConditionExpressionEngine _expressionEngine = expressionEngine;
    private readonly ILogger<SupportSeedService> _logger = logger;

    /// <summary>业务域目录骨架（域编码即目录名，物化路径 <c>/&lt;域&gt;</c>）。</summary>
    private static readonly string[] s_domainFolders =
    [
        SupportFileDomains.Purchase,
        SupportFileDomains.Sales,
        SupportFileDomains.Hr,
        SupportFileDomains.Finance,
        SupportFileDomains.Invoice,
        SupportFileDomains.Temp,
        SupportFileDomains.Unclassified,
    ];

    /// <summary>
    /// 确保当前租户的文件域种子数据齐备（幂等，可重复调用）。
    /// </summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="operatorId">触发操作人（目录所有者记录）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task EnsureSeededAsync(long tenantId, string operatorId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        // 1. 目录骨架：按现有 Normal 目录路径去重补齐（避免 N+1，一次查询现有路径集合）
        var existingPaths = await _db.StorageObjects
            .AsNoTracking()
            .Where(o => o.ObjectType == StorageObjectType.Folder && o.Status == StorageObjectStatus.Normal)
            .Select(o => o.Path)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var existing = existingPaths.ToHashSet(StringComparer.Ordinal);

        var now = DateTimeOffset.UtcNow;
        foreach (var domain in s_domainFolders)
        {
            var path = $"/{domain}";
            if (existing.Contains(path))
            {
                continue;
            }

            _db.StorageObjects.Add(StorageObject.CreateFolder(tenantId, null, domain, domain, path, operatorId));
        }

        // 2. 归类规则 + ACL 模板：仅当租户完全没有规则时种入默认集
        if (!await _db.ClassifyRules.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            var template = new AclTemplate(tenantId, "默认全员可读模板", "common", SerializeEntriesJson());
            _db.AclTemplates.Add(template);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _db.ClassifyRules.AddRange(
                new ClassifyRule(tenantId, "inv", "*", "/invoice/{yyyy}/{MM}", null, protectSource: true, priority: 100),
                new ClassifyRule(tenantId, "pur", "*", "/purchase/{yyyy}/{MM}", template.Id, protectSource: false, priority: 10),
                new ClassifyRule(tenantId, "sal", "*", "/sales/{yyyy}/{MM}", template.Id, protectSource: false, priority: 10),
                new ClassifyRule(tenantId, "hr", "*", "/hr/{yyyy}/{MM}", template.Id, protectSource: false, priority: 10),
                new ClassifyRule(tenantId, "fin", "*", "/finance/{yyyy}/{MM}", template.Id, protectSource: false, priority: 10));
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        LogSeedCompleted(tenantId);
    }

    /// <summary>消息模板种子定义（编码即幂等键；InApp 渠道 v1，渲染解析取 Active 最高版本）。</summary>
    /// <param name="TplCode">模板编码。</param>
    /// <param name="Name">模板名称。</param>
    /// <param name="MsgType">消息类别。</param>
    /// <param name="Subject">标题模板。</param>
    /// <param name="Content">正文模板。</param>
    /// <param name="VarSchema">变量模式 JSON。</param>
    private sealed record MessageTemplateSeed(
        string TplCode, string Name, string MsgType, string Subject, string Content, string VarSchema);

    /// <summary>8 类消息模板（TODO/RESULT/URGE/ALERT/TASK/NOTICE/ANN/PM；InApp 渠道）。</summary>
    private static readonly MessageTemplateSeed[] s_messageTemplates =
    [
        new("msg.todo.generated", "待办任务通知", MessageKinds.Todo,
            "${title}待办", "您有一个新的待办任务：${title}（单号 ${bizNo}），请及时处理。",
            """[{"name":"title","type":"string","required":true},{"name":"bizNo","type":"string","required":false}]"""),
        new("msg.urge.reminder", "催办提醒", MessageKinds.Urge,
            "催办：${title}", "任务 ${title}（单号 ${bizNo}）已逾期，请尽快处理。",
            """[{"name":"title","type":"string","required":true},{"name":"bizNo","type":"string","required":false}]"""),
        new("msg.result.notice", "审批结果通知", MessageKinds.Result,
            "审批结果：${title}", "您提交的 ${title}（单号 ${bizNo}）已${result}。",
            """[{"name":"title","type":"string","required":true},{"name":"bizNo","type":"string","required":false},{"name":"result","type":"string","required":true}]"""),
        new("msg.alert.warning", "系统预警", MessageKinds.Alert,
            "预警通知：${title}", "预警内容：${content}，请关注处理。",
            """[{"name":"title","type":"string","required":true},{"name":"content","type":"string","required":true}]"""),
        new("msg.task.progress", "任务进度通知", MessageKinds.Task,
            "任务进度更新：${title}", "任务 ${title} 进度已更新至 ${percent}%。",
            """[{"name":"title","type":"string","required":true},{"name":"percent","type":"number","required":false}]"""),
        new("msg.notice.system", "系统通知", MessageKinds.Notice,
            "${title}", "${content}",
            """[{"name":"title","type":"string","required":true},{"name":"content","type":"string","required":true}]"""),
        new("msg.ann.broadcast", "全局公告", MessageKinds.Announcement,
            "${title}", "${content}",
            """[{"name":"title","type":"string","required":true},{"name":"content","type":"string","required":true}]"""),
        new("msg.pm.received", "私信消息", MessageKinds.Pm,
            "来自 ${sender} 的私信", "${content}",
            """[{"name":"sender","type":"string","required":true},{"name":"content","type":"string","required":true}]"""),
    ];

    /// <summary>
    /// 确保当前租户的 8 类消息模板齐备（幂等：按编码逐一判存，仅补缺失项，不覆盖租户自定义模板）。
    /// 订阅配置不在此种入：服务侧 <c>GetOrProvisionSubscriptionsAsync</c> 按用户自动补齐（含强制订阅锁定）。
    /// </summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task EnsureMessageTemplatesSeededAsync(long tenantId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        string[] seedCodes = [.. s_messageTemplates.Select(static s => s.TplCode)];
        var existingCodes = await _db.MessageTemplates
            .AsNoTracking()
            .Where(t => seedCodes.Contains(t.TplCode))
            .Select(t => t.TplCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var existing = existingCodes.ToHashSet(StringComparer.Ordinal);

        foreach (var seed in s_messageTemplates)
        {
            if (existing.Contains(seed.TplCode))
            {
                continue;
            }

            _db.MessageTemplates.Add(new MessageTemplateEntity(
                tenantId, seed.TplCode, seed.Name, seed.MsgType, MessageChannel.InApp,
                seed.Subject, seed.Content, seed.VarSchema, version: 1, now));
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        LogMessageSeedCompleted(tenantId, s_messageTemplates.Length - existing.Count);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "租户文件域种子校验完成：{TenantId}")]
    private partial void LogSeedCompleted(long tenantId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "租户消息模板种子校验完成：{TenantId}（补种 {Seeded} 条）")]
    private partial void LogMessageSeedCompleted(long tenantId, int seeded);

    [LoggerMessage(Level = LogLevel.Debug, Message = "租户审批流程种子校验完成：{TenantId}（补种 {Seeded} 条）")]
    private partial void LogApprovalSeedCompleted(long tenantId, int seeded);

    // ============ 审批流程种子（2E 审批中心） ============

    /// <summary>流程节点种子定义。</summary>
    /// <param name="NodeKey">节点键（定义内唯一）。</param>
    /// <param name="NodeName">节点名称。</param>
    /// <param name="ApproverType">审批人类型（ApprovalApproverTypes 字典值）。</param>
    /// <param name="ApproverRef">审批人引用（角色编码 / 快照字段名；可空）。</param>
    /// <param name="SignMode">会签/或签模式。</param>
    /// <param name="TimeoutHours">超时小时数（R8，到期催办）。</param>
    /// <param name="SortNo">排序号。</param>
    /// <param name="SkipIfInitiator">发起人避让开关（R13）。</param>
    private sealed record ApprovalNodeSeed(
        string NodeKey, string NodeName, string ApproverType, string? ApproverRef,
        ApprovalSignMode SignMode, int TimeoutHours, int SortNo, bool SkipIfInitiator = false);

    /// <summary>条件分支种子定义（<see cref="ExpressionText"/> 为空即兜底分支，无运行期求值）。</summary>
    /// <param name="FromNodeKey">来源节点键。</param>
    /// <param name="ToNodeKey">目标节点键（空串 = 终态分支）。</param>
    /// <param name="ExpressionText">条件表达式源文本（null 即兜底分支）。</param>
    /// <param name="Priority">优先级（数值大者先求值）。</param>
    private sealed record ApprovalBranchSeed(string FromNodeKey, string ToNodeKey, string? ExpressionText, int Priority);

    /// <summary>流程定义种子定义（编码即幂等键）。</summary>
    /// <param name="DefCode">定义编码。</param>
    /// <param name="DefName">定义名称。</param>
    /// <param name="BizType">单据类型。</param>
    /// <param name="Nodes">节点清单。</param>
    /// <param name="Branches">分支清单（每条来源线必须含一条兜底，R3）。</param>
    private sealed record ApprovalFlowSeed(
        string DefCode, string DefName, string BizType,
        IReadOnlyList<ApprovalNodeSeed> Nodes, IReadOnlyList<ApprovalBranchSeed> Branches);

    /// <summary>
    /// 4 个默认审批流程。金额阈值（50000 / 200000 / 5000）为示例默认值，
    /// 正式值待 por_parameters 参数域挂接（2F 管控域统一迁参）；
    /// 每条来源线均含兜底分支（无条件线直接以兜底表达），保证求值必达终态（R3）。
    /// </summary>
    private static readonly ApprovalFlowSeed[] s_approvalFlows =
    [
        new("flow.purchase.request", "采购申请审批", "pur_request",
            [
                new("dept_leader", "部门负责人审批", ApprovalApproverTypes.DeptLeader, null,
                    ApprovalSignMode.OrSign, 24, 10, SkipIfInitiator: true),
                new("role_admin", "管理员审批", ApprovalApproverTypes.Role, "admin",
                    ApprovalSignMode.OrSign, 48, 20),
                new("general_manager", "总经理审批", ApprovalApproverTypes.FieldUser, "general_manager",
                    ApprovalSignMode.OrSign, 72, 30),
            ],
            [
                new("dept_leader", "role_admin", "total_amount >= 50000", 20),
                new("dept_leader", "", null, 0),
                new("role_admin", "general_manager", "total_amount >= 200000", 20),
                new("role_admin", "", null, 0),
                new("general_manager", "", null, 0),
            ]),
        new("flow.expense.claim", "费用报销审批", "expense_claim",
            [
                new("dept_leader", "部门负责人审批", ApprovalApproverTypes.DeptLeader, null,
                    ApprovalSignMode.OrSign, 24, 10, SkipIfInitiator: true),
                new("role_admin", "管理员审批", ApprovalApproverTypes.Role, "admin",
                    ApprovalSignMode.OrSign, 48, 20),
            ],
            [
                new("dept_leader", "role_admin", "total_amount >= 5000", 20),
                new("dept_leader", "", null, 0),
                new("role_admin", "", null, 0),
            ]),
        new("flow.contract.review", "合同评审审批", "contract_review",
            [
                new("dept_leader", "部门负责人评审", ApprovalApproverTypes.DeptLeader, null,
                    ApprovalSignMode.Countersign, 48, 10),
                new("role_admin", "管理员评审", ApprovalApproverTypes.Role, "admin",
                    ApprovalSignMode.Countersign, 48, 20),
                new("legal_officer", "法务评审", ApprovalApproverTypes.FieldUser, "legal_officer",
                    ApprovalSignMode.Countersign, 72, 30),
            ],
            [
                new("dept_leader", "role_admin", null, 0),
                new("role_admin", "legal_officer", null, 0),
                new("legal_officer", "", null, 0),
            ]),
        new("flow.labour.salary", "劳务工资表审批", "labour_salary",
            [
                new("site_manager", "项目负责人审批", ApprovalApproverTypes.FieldUser, "site_manager",
                    ApprovalSignMode.Countersign, 48, 10),
                new("role_admin", "管理员审批", ApprovalApproverTypes.Role, "admin",
                    ApprovalSignMode.Countersign, 72, 20),
            ],
            [
                new("site_manager", "role_admin", null, 0),
                new("role_admin", "", null, 0),
            ]),
    ];

    /// <summary>
    /// 确保当前租户的 4 个默认审批流程齐备（幂等：按定义编码判存，仅补缺失项，直接以 Enabled 状态种入）。
    /// 条件分支 expr_json 经条件表达式引擎 Parse 产出 AST JSON，运行期 <c>ConditionExpression.FromJson</c> 反序列化求值（R3）。
    /// </summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task EnsureApprovalFlowsSeededAsync(long tenantId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        string[] seedCodes = [.. s_approvalFlows.Select(static f => f.DefCode)];
        var existingCodes = await _db.FlowDefinitions
            .AsNoTracking()
            .Where(d => seedCodes.Contains(d.DefCode))
            .Select(d => d.DefCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var existing = existingCodes.ToHashSet(StringComparer.Ordinal);

        foreach (var seed in s_approvalFlows)
        {
            if (existing.Contains(seed.DefCode))
            {
                continue;
            }

            // 先落定义拿雪花 Id（Id 于 SaveChanges 时填充），再挂节点与分支
            var definition = new ApprovalFlowDefinition(
                tenantId, seed.DefCode, seed.DefName, seed.BizType, "[]", version: 1,
                SerializeFlowDesignJson(seed), now);
            definition.SetStatus(ApprovalFlowStatus.Enabled, now);
            _db.FlowDefinitions.Add(definition);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            foreach (var node in seed.Nodes)
            {
                _db.FlowNodes.Add(new ApprovalFlowNode(
                    tenantId, definition.Id, node.NodeKey, ApprovalNodeType.Approval, node.NodeName,
                    node.ApproverType, node.ApproverRef, node.SignMode,
                    node.TimeoutHours, ApprovalTimeoutAction.Urge, allowAddSign: true,
                    ApprovalRejectMode.StepBack, node.SortNo, node.SkipIfInitiator));
            }

            foreach (var branch in seed.Branches)
            {
                _db.FlowBranches.Add(new ApprovalFlowBranch(
                    tenantId, definition.Id, branch.FromNodeKey, branch.ToNodeKey,
                    branch.ExpressionText is null
                        ? string.Empty
                        : _expressionEngine.Parse(branch.ExpressionText).ToJson(),
                    branch.Priority, isDefault: branch.ExpressionText is null));
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        LogApprovalSeedCompleted(tenantId, s_approvalFlows.Length - existing.Count);
    }

    /// <summary>生成流程定义设计器摘要 JSON（节点与分支结构化描述，供流程设计器初始化展示与版本对比）。</summary>
    private static string SerializeFlowDesignJson(ApprovalFlowSeed seed)
        => JsonSerializer.Serialize(new
        {
            nodes = seed.Nodes.Select(static n => new
            {
                key = n.NodeKey, name = n.NodeName, type = "approval",
                approverType = n.ApproverType, approverRef = n.ApproverRef,
                signMode = n.SignMode.ToString(), timeoutHours = n.TimeoutHours,
                sortNo = n.SortNo, skipIfInitiator = n.SkipIfInitiator,
            }),
            branches = seed.Branches.Select(static b => new
            {
                from = b.FromNodeKey, to = b.ToNodeKey,
                condition = b.ExpressionText, priority = b.Priority,
                isDefault = b.ExpressionText is null,
            }),
        }, QlxJsonDefaults.Default);

    /// <summary>默认 ACL 模板条目 JSON：全员 Allow Read（写/删仍归所有者与管理员）。</summary>
    private static string SerializeEntriesJson()
    {
        var entries = new List<AclEntryPayload>
        {
            new((int)Qlx.Abstractions.Files.FileSubjectType.User, "*",
                (int)Qlx.Abstractions.Files.FilePermission.Read,
                (int)Qlx.BuildingBlocks.Files.AceType.Allow, Propagate: true),
        };
        return JsonSerializer.Serialize(entries, QlxJsonDefaults.Default);
    }
}
