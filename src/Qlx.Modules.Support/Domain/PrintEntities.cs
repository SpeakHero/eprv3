/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 13:00:00 +08:00 中国标准时间 星期五
 * 描述：打印中心领域实体（prt_ 前缀 1 张表）：打印模板（含占位符 HTML + 版本 + 状态）
 */

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.Modules.Support.Domain;

/// <summary>打印模板状态（prt_print_templates.status）。</summary>
public enum PrintTemplateStatus
{
    /// <summary>草稿（编辑中，不可被渲染命中）。</summary>
    Draft = 0,

    /// <summary>启用（渲染入口唯一承接状态）。</summary>
    Enabled = 1,

    /// <summary>停用（不再承接渲染；历史打印已完成的不受影响）。</summary>
    Disabled = 2,
}

/// <summary>
/// 打印模板实体（表 <c>prt_print_templates</c>）。ContentHtml 为含占位符的 HTML：
/// <c>{{ 模型字段 }}</c> 为 Scriban 数据占位；<c>{{qrcode:内容}}</c> 为溯源二维码占位
/// （渲染时生成内嵌 base64 PNG，语法见 <c>QrCodePlaceholderRenderer</c>）。
/// 内容更新时 Version 自增（同码唯一版本链）。
/// </summary>
[Index(nameof(TenantId), nameof(TemplateCode), nameof(Version), IsUnique = true)]
[Index(nameof(TenantId), nameof(TemplateCode), nameof(Status))]
[TenantTablePrefix("prt_")]
public sealed class PrintTemplate : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>模板编码（同租户唯一，如 <c>purchase.order.print</c>）。</summary>
    [MaxLength(100)]
    public string TemplateCode { get; internal set; } = string.Empty;

    /// <summary>模板名称。</summary>
    [MaxLength(200)]
    public string Name { get; internal set; } = string.Empty;

    /// <summary>业务类型（模块/单据归类，如 <c>purchase.order</c>；契约 PrintTemplateInfo.ModuleCode 由此承载）。</summary>
    [MaxLength(64)]
    public string BizType { get; internal set; } = string.Empty;

    /// <summary>模板 HTML（含 Scriban 数据占位符与二维码占位符）。</summary>
    [MaxLength(200_000)]
    public string ContentHtml { get; internal set; } = string.Empty;

    /// <summary>附加 CSS（可空，渲染时并入 HTML 头）。</summary>
    [MaxLength(50_000)]
    public string? Css { get; internal set; }

    /// <summary>模板状态。</summary>
    public PrintTemplateStatus Status { get; internal set; } = PrintTemplateStatus.Draft;

    /// <summary>版本号（内容更新自增，同码唯一）。</summary>
    public int Version { get; internal set; } = 1;

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; internal set; }

    /// <summary>最近更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public PrintTemplate() { }

    /// <summary>创建打印模板（初始 Draft，版本 1）。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="templateCode">模板编码。</param>
    /// <param name="name">模板名称。</param>
    /// <param name="bizType">业务类型。</param>
    /// <param name="contentHtml">模板 HTML。</param>
    /// <param name="css">附加 CSS（可空）。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public PrintTemplate(
        long tenantId, string templateCode, string name, string bizType,
        string contentHtml, string? css, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(bizType);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHtml);

        TenantId = tenantId;
        TemplateCode = templateCode;
        Name = name;
        BizType = bizType;
        ContentHtml = contentHtml;
        Css = css;
        Status = PrintTemplateStatus.Draft;
        Version = 1;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>更新模板内容（版本自增，状态保持不变）。</summary>
    /// <param name="name">模板名称。</param>
    /// <param name="bizType">业务类型。</param>
    /// <param name="contentHtml">模板 HTML。</param>
    /// <param name="css">附加 CSS（可空）。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public void UpdateContent(string name, string bizType, string contentHtml, string? css, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHtml);

        Name = name;
        BizType = bizType;
        ContentHtml = contentHtml;
        Css = css;
        Version++;
        UpdatedAt = now;
    }

    /// <summary>设置模板状态。</summary>
    /// <param name="status">目标状态。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public void SetStatus(PrintTemplateStatus status, DateTimeOffset now)
    {
        Status = status;
        UpdatedAt = now;
    }
}
