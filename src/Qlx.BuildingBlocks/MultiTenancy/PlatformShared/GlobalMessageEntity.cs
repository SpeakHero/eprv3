/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 06:30:00 +08:00 中国标准时间 星期五
 * 描述：共享库全局消息实体（glb_messages）——平台公告/全局消息只存共享库一份，租户只读
 */

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Messages;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 全局消息实体（表 <c>glb_messages</c>，共享库 glb_ 前缀）。平台级公告 / 全局通知，全部租户可见，
/// 正文只存本表一份；租户侧已读/确认/删除状态走租户库 <c>msg_global_state</c> 叠加（§5.11）。
/// 平台撤回（Revoked=true）后租户读取侧过滤。
/// <para>列名 snake_case 由 <see cref="TenantDbContext"/> 基类统一转换；
/// 无 TenantId 属性（平台级数据），基类不注入租户过滤。</para>
/// </summary>
[Index(nameof(PublishedAt))]
[Index(nameof(MsgType), nameof(PublishedAt))]
[TenantTablePrefix("glb_")]
public sealed class GlobalMessageEntity
{
    /// <summary>全局消息 Id（雪花 ID；租户侧引用其混淆串作为 GlobalId）。</summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long Id { get; set; }

    /// <summary>消息类别（ANN 公告 / NOTICE 通知 / ALERT 预警等）。</summary>
    [MaxLength(32)]
    public required string MsgType { get; set; }

    /// <summary>标题。</summary>
    [MaxLength(200)]
    public required string Subject { get; set; }

    /// <summary>正文。</summary>
    [MaxLength(4000)]
    public required string Content { get; set; }

    /// <summary>优先级。</summary>
    public MessagePriority Priority { get; set; } = MessagePriority.Normal;

    /// <summary>是否强制确认公告（true=租户不可删除 / 不可拒收，仅可确认）。</summary>
    public bool ForceAccept { get; set; }

    /// <summary>是否已被平台撤回（租户读取侧过滤）。</summary>
    public bool Revoked { get; set; }

    /// <summary>发布人（平台操作账号）。</summary>
    [MaxLength(128)]
    public required string PublishedBy { get; set; }

    /// <summary>发布时间（UTC）。</summary>
    public DateTimeOffset PublishedAt { get; set; }

    /// <summary>撤回时间（UTC；可空）。</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>生效起始时间（UTC；可空，预留定时发布）。</summary>
    public DateTimeOffset? EffectiveFrom { get; set; }

    /// <summary>失效时间（UTC；可空，到期不再下发）。</summary>
    public DateTimeOffset? EffectiveTo { get; set; }
}
