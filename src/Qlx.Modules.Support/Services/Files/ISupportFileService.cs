/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 01:40:00 +08:00 中国标准时间 星期五
 * 描述：Support 模块文件存储门面服务契约（Controller 消费）+ DTO（FileId 声明 long，经全局 JSON 元数据约定自动混淆）
 */

using Qlx.Abstractions.Files;

namespace Qlx.Modules.Support.Services.Files;

/// <summary>
/// Support 模块文件存储门面服务（Controller 主入口）。
/// S18/D22 红线：全部对外方法内部只经 <see cref="Qlx.Abstractions.Jobs.IJobScheduler.SubmitAsync"/>，
/// 禁止直接调 <see cref="Qlx.BuildingBlocks.Files.IFileStorageBackend"/>。
/// 当前用户与租户上下文由注入的 ICurrentUserContext / ITenantContext 提供。
/// </summary>
public interface ISupportFileService
{
    /// <summary>上传文件（file.upload，SYNC：哈希 + 秒传 + 归类 + 落盘 + ACL）。</summary>
    Task<SupportFileUploadResult> UploadAsync(FileUploadRequest request, string? bizModule, string? docType, CancellationToken cancellationToken);

    /// <summary>查询文件元数据（file.metadata，SYNC，需 Read 权限）。</summary>
    Task<SupportFileMetadata> GetMetadataAsync(long fileId, CancellationToken cancellationToken);

    /// <summary>按父目录列出子项（无 parentId 列根目录）；keyword 模式全租户按名检索。</summary>
    Task<IReadOnlyList<SupportFileMetadata>> ListAsync(long? parentId, string? keyword, CancellationToken cancellationToken);

    /// <summary>打开下载流（file.download，SYNC，需 Read 权限；流由调用方负责释放）。</summary>
    Task<SupportFileContent> OpenDownloadAsync(long fileId, CancellationToken cancellationToken);

    /// <summary>打开预览流（file.preview，2B 与下载同流）。</summary>
    Task<SupportFileContent> OpenPreviewAsync(long fileId, CancellationToken cancellationToken);

    /// <summary>软删进回收站（file.delete，SYNC，需 Delete 权限）。</summary>
    Task<SupportFileMetadata> DeleteAsync(long fileId, CancellationToken cancellationToken);

    /// <summary>从回收站还原（file.restore，SYNC，需 Write 权限）。</summary>
    Task<SupportFileMetadata> RestoreAsync(long fileId, CancellationToken cancellationToken);

    /// <summary>回收站彻底删除（file.purge，SYNC，需 Delete 权限；物理清理不可恢复）。</summary>
    Task<SupportFileMetadata> PurgeAsync(long fileId, CancellationToken cancellationToken);

    /// <summary>回收站列表（file.recycle.list，SYNC；管理员全量，普通用户仅本人）。</summary>
    Task<IReadOnlyList<SupportFileMetadata>> ListRecycleAsync(CancellationToken cancellationToken);
}

/// <summary>上传结果（FileId/上传边界输出，JSON 序列化时经全局约定自动混淆为字符串）。</summary>
public sealed record SupportFileUploadResult(
    long FileId,
    string StorageKey,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Hash,
    string Path,
    string BizDomain,
    bool Deduplicated,
    string? Warning);

/// <summary>文件元数据 DTO。</summary>
public sealed record SupportFileMetadata(
    long FileId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Hash,
    string Path,
    string BizDomain,
    int CurrentVersion,
    string OwnerId,
    string Status,
    bool IsProtected,
    DateTimeOffset? RecycledAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>文件内容（进程内流式载荷；FileId 用于响应头/审计）。</summary>
public sealed record SupportFileContent(
    long FileId,
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes);
