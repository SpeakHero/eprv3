/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 23:15:00 +08:00 中国标准时间 星期四
 * 描述：临时文件实体（fil_temp_files）+ 分享链接实体（fil_share_links）+ 文件操作日志实体（fil_file_op_logs）
 */

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.Modules.Support.Domain;

/// <summary>临时文件状态（规则 R4：TTL 过期由 2C file.temp.purge 清理）。</summary>
public enum TempFileStatus
{
    /// <summary>有效。</summary>
    Active = 1,

    /// <summary>已消费（转正为正式对象）。</summary>
    Consumed = 2,

    /// <summary>已过期清理。</summary>
    Expired = 3,
}

/// <summary>
/// 临时文件实体（表 <c>fil_temp_files</c>）。仅发起人与租户管理员可读（规则 R4）。
/// </summary>
[TenantTablePrefix("fil_")]
[Index(nameof(TenantId), nameof(ExpireAt))]
[Index(nameof(TenantId), nameof(ObjectId))]
public sealed class TempFile : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>关联存储对象 Id。</summary>
    public long ObjectId { get; internal set; }

    /// <summary>产生该临时文件的任务执行 Id（可空）。</summary>
    [MaxLength(64)]
    public string? ExecId { get; internal set; }

    /// <summary>发起人用户标识。</summary>
    [MaxLength(64)]
    public string OwnerId { get; internal set; } = string.Empty;

    /// <summary>过期时间（TTL 基准）。</summary>
    public DateTimeOffset ExpireAt { get; internal set; }

    /// <summary>状态。</summary>
    public TempFileStatus Status { get; internal set; } = TempFileStatus.Active;

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public TempFile() { }

    /// <summary>创建临时文件。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="objectId">关联存储对象 Id。</param>
    /// <param name="execId">产生该临时文件的任务执行 Id（可空）。</param>
    /// <param name="ownerId">发起人用户标识。</param>
    /// <param name="expireAt">过期时间。</param>
    public TempFile(long tenantId, long objectId, string? execId, string ownerId, DateTimeOffset expireAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(objectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        TenantId = tenantId;
        ObjectId = objectId;
        ExecId = execId;
        OwnerId = ownerId;
        ExpireAt = expireAt;
    }

    /// <summary>判定是否已过期（规则 R4 TTL 判定）。</summary>
    public bool IsExpired(DateTimeOffset now) => Status == TempFileStatus.Active && now >= ExpireAt;
}

/// <summary>
/// 文件分享链接实体（表 <c>fil_share_links</c>）。
/// </summary>
[TenantTablePrefix("fil_")]
[Index(nameof(TenantId), nameof(Token), IsUnique = true)]
[Index(nameof(TenantId), nameof(ObjectId))]
public sealed class ShareLink : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>关联存储对象 Id。</summary>
    public long ObjectId { get; internal set; }

    /// <summary>分享令牌。</summary>
    [MaxLength(64)]
    public string Token { get; internal set; } = string.Empty;

    /// <summary>过期时间。</summary>
    public DateTimeOffset ExpireAt { get; internal set; }

    /// <summary>访问口令哈希（可空：无口令）。</summary>
    [MaxLength(200)]
    public string? PasswordHash { get; internal set; }

    /// <summary>访问次数。</summary>
    public long AccessCount { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public ShareLink() { }

    /// <summary>创建分享链接。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="objectId">关联存储对象 Id。</param>
    /// <param name="token">分享令牌。</param>
    /// <param name="expireAt">过期时间。</param>
    /// <param name="passwordHash">访问口令哈希（可空：无口令）。</param>
    public ShareLink(long tenantId, long objectId, string token, DateTimeOffset expireAt, string? passwordHash)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(objectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        TenantId = tenantId;
        ObjectId = objectId;
        Token = token;
        ExpireAt = expireAt;
        PasswordHash = passwordHash;
    }
}

/// <summary>
/// 文件操作日志实体（表 <c>fil_file_op_logs</c>，只增不改）。
/// </summary>
[TenantTablePrefix("fil_")]
[Index(nameof(TenantId), nameof(ObjectId))]
[Index(nameof(TenantId), nameof(CreatedAt))]
public sealed class FileOpLog : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>关联存储对象 Id（0 表示租户级操作）。</summary>
    public long ObjectId { get; internal set; }

    /// <summary>操作类型（UPLOAD/DOWNLOAD/PREVIEW/RECYCLE/RESTORE/ACL_SET/...）。</summary>
    [MaxLength(50)]
    public string Action { get; internal set; } = string.Empty;

    /// <summary>操作人用户标识。</summary>
    [MaxLength(64)]
    public string OperatorId { get; internal set; } = string.Empty;

    /// <summary>操作来源 IP。</summary>
    [MaxLength(64)]
    public string? Ip { get; internal set; }

    /// <summary>操作明细（JSON 或文本）。</summary>
    [MaxLength(2000)]
    public string? Detail { get; internal set; }

    /// <summary>操作时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public FileOpLog() { }

    /// <summary>创建文件操作日志。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="objectId">关联存储对象 Id（0 表示租户级操作）。</param>
    /// <param name="action">操作类型。</param>
    /// <param name="operatorId">操作人用户标识。</param>
    /// <param name="ip">操作来源 IP。</param>
    /// <param name="detail">操作明细。</param>
    /// <param name="createdAt">操作时间（UTC）。</param>
    public FileOpLog(long tenantId, long objectId, string action, string operatorId, string? ip, string? detail, DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);
        TenantId = tenantId;
        ObjectId = objectId;
        Action = action;
        OperatorId = operatorId;
        Ip = ip;
        Detail = detail;
        CreatedAt = createdAt;
    }
}
