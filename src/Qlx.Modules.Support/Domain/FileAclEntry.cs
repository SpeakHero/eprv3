/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 23:05:00 +08:00 中国标准时间 星期四
 * 描述：文件 ACL 条目实体（fil_acl_entries）——ACE 类型复用 BuildingBlocks.Files.AceType
 */

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.Modules.Support.Domain;

/// <summary>
/// 文件 ACL 条目实体（表 <c>fil_acl_entries</c>）。
/// 主体类型/权限位复用 Abstractions.Files 的 <see cref="Qlx.Abstractions.Files.FileSubjectType"/>
/// 与 <see cref="Qlx.Abstractions.Files.FilePermission"/>（int 存储）。
/// </summary>
[TenantTablePrefix("fil_")]
[Index(nameof(TenantId), nameof(ObjectId))]
[Index(nameof(TenantId), nameof(SubjectType), nameof(SubjectId))]
public sealed class FileAclEntry : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>所属存储对象 Id。</summary>
    public long ObjectId { get; internal set; }

    /// <summary>主体类型（用户/角色/组织）。</summary>
    public Qlx.Abstractions.Files.FileSubjectType SubjectType { get; internal set; }

    /// <summary>主体 Id（用户标识 / 角色 Id / 组织 Id；<c>*</c> 表示全部主体）。</summary>
    [MaxLength(64)]
    public string SubjectId { get; internal set; } = string.Empty;

    /// <summary>权限位掩码（<see cref="Qlx.Abstractions.Files.FilePermission"/> 的 int 值）。</summary>
    public int PermMask { get; internal set; }

    /// <summary>ACE 类型（Allow/Deny，见 <see cref="Qlx.BuildingBlocks.Files.AceType"/>）。</summary>
    public Qlx.BuildingBlocks.Files.AceType AceType { get; internal set; }

    /// <summary>继承来源对象物化路径（本条目自该祖先继承而来；直接授予为 null）。</summary>
    [MaxLength(1000)]
    public string? InheritSrc { get; internal set; }

    /// <summary>是否向子级传播。</summary>
    public bool Propagate { get; internal set; } = true;

    /// <summary>授予人。</summary>
    [MaxLength(64)]
    public string? GrantedBy { get; internal set; }

    /// <summary>授予时间。</summary>
    public DateTimeOffset GrantedAt { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public FileAclEntry() { }

    /// <summary>创建 ACL 条目。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="objectId">所属存储对象 Id。</param>
    /// <param name="subjectType">主体类型。</param>
    /// <param name="subjectId">主体 Id（<c>*</c> 表示全部主体）。</param>
    /// <param name="permMask">权限位掩码。</param>
    /// <param name="aceType">ACE 类型（Allow/Deny）。</param>
    /// <param name="propagate">是否向子级传播。</param>
    /// <param name="inheritSrc">继承来源对象物化路径（直接授予为 null）。</param>
    /// <param name="grantedBy">授予人。</param>
    /// <param name="grantedAt">授予时间。</param>
    public FileAclEntry(long tenantId, long objectId, Qlx.Abstractions.Files.FileSubjectType subjectType,
        string subjectId, Qlx.Abstractions.Files.FilePermission permMask, Qlx.BuildingBlocks.Files.AceType aceType,
        bool propagate, string? inheritSrc, string? grantedBy, DateTimeOffset grantedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(objectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        TenantId = tenantId;
        ObjectId = objectId;
        SubjectType = subjectType;
        SubjectId = subjectId;
        PermMask = (int)permMask;
        AceType = aceType;
        Propagate = propagate;
        InheritSrc = inheritSrc;
        GrantedBy = grantedBy;
        GrantedAt = grantedAt;
    }
}
