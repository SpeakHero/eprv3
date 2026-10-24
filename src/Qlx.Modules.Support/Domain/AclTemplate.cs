/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 23:10:00 +08:00 中国标准时间 星期四
 * 描述：ACL 模板实体（fil_acl_templates）+ 归类规则实体（fil_classify_rules）+ 启停状态枚举
 */

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.Modules.Support.Domain;

/// <summary>支撑域配置启停状态（ACL 模板 / 归类规则共用）。</summary>
public enum SupportConfigStatus
{
    /// <summary>启用。</summary>
    Enabled = 1,

    /// <summary>停用。</summary>
    Disabled = 2,
}

/// <summary>
/// ACL 模板实体（表 <c>fil_acl_templates</c>）。EntriesJson 为
/// <see cref="Qlx.Abstractions.Files.FileAclEntryInfo"/> 形态条目数组的 JSON 快照。
/// </summary>
[TenantTablePrefix("fil_")]
[Index(nameof(TenantId), nameof(BizDomain))]
public sealed class AclTemplate : Qlx.Abstractions.Domain.TenantAuditableEntity
{
    /// <summary>模板名称（租户内唯一标识语义）。</summary>
    [MaxLength(100)]
    public string TplName { get; internal set; } = string.Empty;

    /// <summary>适用业务域。</summary>
    [MaxLength(50)]
    public string BizDomain { get; internal set; } = string.Empty;

    /// <summary>条目 JSON（序列化的 ACL 条目数组）。</summary>
    [MaxLength(4000)]
    public string EntriesJson { get; internal set; } = string.Empty;

    /// <summary>启停状态。</summary>
    public SupportConfigStatus Status { get; internal set; } = SupportConfigStatus.Enabled;

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public AclTemplate() { }

    /// <summary>创建 ACL 模板。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="tplName">模板名称。</param>
    /// <param name="bizDomain">适用业务域。</param>
    /// <param name="entriesJson">条目 JSON 快照。</param>
    public AclTemplate(long tenantId, string tplName, string bizDomain, string entriesJson)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tplName);
        ArgumentException.ThrowIfNullOrWhiteSpace(bizDomain);
        ArgumentException.ThrowIfNullOrWhiteSpace(entriesJson);
        TenantId = tenantId;
        TplName = tplName;
        BizDomain = bizDomain;
        EntriesJson = entriesJson;
    }
}

/// <summary>
/// 归类规则实体（表 <c>fil_classify_rules</c>）。上传时按 BizModule + DocType 匹配，
/// Priority 大者优先；未命中落 <c>/unclassified</c>（规则 R2）。
/// </summary>
[TenantTablePrefix("fil_")]
[Index(nameof(TenantId), nameof(BizModule), nameof(DocType))]
public sealed class ClassifyRule : Qlx.Abstractions.Domain.TenantAuditableEntity
{
    /// <summary>业务模块编码（pur/sal/fin/inv...）。</summary>
    [MaxLength(50)]
    public string BizModule { get; internal set; } = string.Empty;

    /// <summary>单据类型编码；<c>*</c> 表示该模块全部类型。</summary>
    [MaxLength(50)]
    public string DocType { get; internal set; } = string.Empty;

    /// <summary>目标目录模板（支持 <c>{yyyy}</c>/<c>{MM}</c> 占位，如 <c>/purchase/{yyyy}/{MM}</c>）。</summary>
    [MaxLength(500)]
    public string TargetPath { get; internal set; } = string.Empty;

    /// <summary>关联 ACL 模板 Id（可空：仅归类不套模板）。</summary>
    public long? AclTplId { get; internal set; }

    /// <summary>是否保护源文件（保护目录：DENY 写/删 ACE + 物理只读，规则 R5）。</summary>
    public bool ProtectSource { get; internal set; }

    /// <summary>匹配优先级（大者优先）。</summary>
    public int Priority { get; internal set; }

    /// <summary>启停状态。</summary>
    public SupportConfigStatus Status { get; internal set; } = SupportConfigStatus.Enabled;

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public ClassifyRule() { }

    /// <summary>创建归类规则。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="bizModule">业务模块编码。</param>
    /// <param name="docType">单据类型编码（<c>*</c> 表示全部）。</param>
    /// <param name="targetPath">目标目录模板。</param>
    /// <param name="aclTplId">关联 ACL 模板 Id（可空）。</param>
    /// <param name="protectSource">是否保护源文件。</param>
    /// <param name="priority">匹配优先级（大者优先）。</param>
    public ClassifyRule(long tenantId, string bizModule, string docType, string targetPath,
        long? aclTplId, bool protectSource, int priority)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bizModule);
        ArgumentException.ThrowIfNullOrWhiteSpace(docType);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        TenantId = tenantId;
        BizModule = bizModule;
        DocType = docType;
        TargetPath = targetPath;
        AclTplId = aclTplId;
        ProtectSource = protectSource;
        Priority = priority;
    }
}
