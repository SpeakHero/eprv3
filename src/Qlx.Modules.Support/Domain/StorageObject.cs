/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 23:00:00 +08:00 中国标准时间 星期四
 * 描述：文件存储对象实体（fil_storage_objects）+ 文件版本实体（fil_file_versions）+ 相关枚举
 */

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.Modules.Support.Domain;

/// <summary>存储对象类型。</summary>
public enum StorageObjectType
{
    /// <summary>目录。</summary>
    Folder = 1,

    /// <summary>文件。</summary>
    File = 2,
}

/// <summary>存储对象生命周期状态（规则 R3：删除 = NORMAL→RECYCLED；PURGED 由 2C 清理任务执行）。</summary>
public enum StorageObjectStatus
{
    /// <summary>正常。</summary>
    Normal = 1,

    /// <summary>回收站。</summary>
    Recycled = 2,

    /// <summary>已物理清理。</summary>
    Purged = 3,
}

/// <summary>
/// 文件存储对象实体（表 <c>fil_storage_objects</c>）。目录与文件统一建模，
/// 回收站以 Status 承载（不使用 IsDeleted，避免与全局软删过滤叠加）。
/// </summary>
[TenantTablePrefix("fil_")]
[Index(nameof(TenantId), nameof(ParentId))]
[Index(nameof(TenantId), nameof(Hash))]
[Index(nameof(TenantId), nameof(Status))]
public sealed class StorageObject : Qlx.Abstractions.Domain.TenantAuditableEntity
{
    /// <summary>父目录 Id；根目录为 null。</summary>
    public long? ParentId { get; internal set; }

    /// <summary>对象类型（目录/文件）。</summary>
    public StorageObjectType ObjectType { get; internal set; }

    /// <summary>对象名称（含扩展名）。</summary>
    [MaxLength(255)]
    public string Name { get; internal set; } = string.Empty;

    /// <summary>业务域（采购/销售/财务/票据/临时/未归类等目录域编码）。</summary>
    [MaxLength(50)]
    public string BizDomain { get; internal set; } = string.Empty;

    /// <summary>物化路径（如 <c>/purchase/2026/10/a.pdf</c>）。</summary>
    [MaxLength(1000)]
    public string Path { get; internal set; } = string.Empty;

    /// <summary>内容 SHA-256 哈希（十六进制，秒传判据；目录为空）。</summary>
    [MaxLength(64)]
    public string? Hash { get; internal set; }

    /// <summary>文件大小（字节；目录为 0）。</summary>
    public long Size { get; internal set; }

    /// <summary>MIME 类型。</summary>
    [MaxLength(200)]
    public string? Mime { get; internal set; }

    /// <summary>当前版本号（从 1 开始）。</summary>
    public int CurrentVersion { get; internal set; }

    /// <summary>所有者用户标识（ICurrentUserContext.UserId 字符串）。</summary>
    [MaxLength(64)]
    public string OwnerId { get; internal set; } = string.Empty;

    /// <summary>是否继承父目录 ACL（规则 R5 继承语义开关）。</summary>
    public bool InheritAcl { get; internal set; } = true;

    /// <summary>生命周期状态。</summary>
    public StorageObjectStatus Status { get; internal set; } = StorageObjectStatus.Normal;

    /// <summary>
    /// 物理文件引用计数（秒传复用同一 StorageKey 的对象数；引用计数归零才允许 2C 物理删除，规则 R6）。
    /// </summary>
    public int RefCount { get; internal set; } = 1;

    /// <summary>进入回收站时间（规则 R3 起算 RecycleDays 的基准）。</summary>
    public DateTimeOffset? RecycledAt { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public StorageObject() { }

    /// <summary>创建文件对象。</summary>
    public StorageObject(long tenantId, long? parentId, string name, string bizDomain, string path,
        string? hash, long size, string? mime, string ownerId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(bizDomain);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        TenantId = tenantId;
        ParentId = parentId;
        ObjectType = StorageObjectType.File;
        Name = name;
        BizDomain = bizDomain;
        Path = path;
        Hash = hash;
        Size = size;
        Mime = mime;
        CurrentVersion = 1;
        OwnerId = ownerId;
    }

    /// <summary>创建目录对象。</summary>
    public static StorageObject CreateFolder(long tenantId, long? parentId, string name, string bizDomain, string path, string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        var folder = new StorageObject
        {
            TenantId = tenantId,
            ParentId = parentId,
            ObjectType = StorageObjectType.Folder,
            Name = name,
            BizDomain = bizDomain,
            Path = path,
            OwnerId = ownerId,
            CurrentVersion = 0,
        };
        return folder;
    }

    /// <summary>软删进回收站（规则 R3）。</summary>
    public void Recycle(DateTimeOffset now)
    {
        Status = StorageObjectStatus.Recycled;
        RecycledAt = now;
    }

    /// <summary>从回收站还原。</summary>
    public void Restore()
    {
        Status = StorageObjectStatus.Normal;
        RecycledAt = null;
    }

    /// <summary>标记物理清理完成（仅 2C 清理任务调用）。</summary>
    public void MarkPurged() => Status = StorageObjectStatus.Purged;
}

/// <summary>
/// 文件版本实体（表 <c>fil_file_versions</c>）。复合主键 (TenantId, ObjectId, VerNo)；
/// 同租户同哈希秒传查询走 (TenantId, Hash) 索引。
/// </summary>
[TenantTablePrefix("fil_")]
[PrimaryKey(nameof(TenantId), nameof(ObjectId), nameof(VerNo))]
[Index(nameof(TenantId), nameof(Hash))]
public sealed class FileVersion : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>所属存储对象 Id。</summary>
    public long ObjectId { get; internal set; }

    /// <summary>版本号（从 1 开始）。</summary>
    public int VerNo { get; internal set; }

    /// <summary>内容 SHA-256 哈希（十六进制）。</summary>
    [MaxLength(64)]
    public string Hash { get; internal set; } = string.Empty;

    /// <summary>版本大小（字节）。</summary>
    public long Size { get; internal set; }

    /// <summary>存储键（后端物理定位键，hash 分片）。</summary>
    [MaxLength(200)]
    public string StorageKey { get; internal set; } = string.Empty;

    /// <summary>版本创建人。</summary>
    [MaxLength(64)]
    public string? CreatedBy { get; internal set; }

    /// <summary>版本创建时间。</summary>
    public DateTimeOffset CreatedAt { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public FileVersion() { }

    /// <summary>创建文件版本。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="objectId">所属存储对象 Id。</param>
    /// <param name="verNo">版本号（从 1 开始）。</param>
    /// <param name="hash">内容 SHA-256 哈希（十六进制）。</param>
    /// <param name="size">版本大小（字节）。</param>
    /// <param name="storageKey">存储键。</param>
    /// <param name="createdBy">版本创建人。</param>
    /// <param name="createdAt">版本创建时间。</param>
    public FileVersion(long tenantId, long objectId, int verNo, string hash, long size, string storageKey, string? createdBy, DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(objectId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(verNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        TenantId = tenantId;
        ObjectId = objectId;
        VerNo = verNo;
        Hash = hash;
        Size = size;
        StorageKey = storageKey;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }
}
