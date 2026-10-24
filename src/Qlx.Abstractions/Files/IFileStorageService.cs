/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:50:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:50:00 +08:00 中国标准时间 星期三
 * 描述：文件存储 / ACL 契约
 */

namespace Qlx.Abstractions.Files;

/// <summary>
/// 文件存储服务接口。
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// 上传文件。
    /// </summary>
    /// <param name="request">上传请求（含流与元数据）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>文件引用（含存储键、大小、哈希等）。</returns>
    Task<FileReference> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 下载文件流。
    /// </summary>
    /// <param name="storageKey">存储键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>文件流（调用方负责 Dispose）。</returns>
    Task<Stream> DownloadAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>
    /// 删除文件。
    /// </summary>
    /// <param name="storageKey">存储键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>
    /// 生成临时下载链接（URL 有效期可配置）。
    /// </summary>
    /// <param name="storageKey">存储键。</param>
    /// <param name="expiration">链接有效期。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>临时下载 URL。</returns>
    Task<string> GetTemporaryUrlAsync(string storageKey, TimeSpan expiration, CancellationToken cancellationToken);
}

/// <summary>
/// 文件 ACL 服务接口。
/// </summary>
public interface IFileAclService
{
    /// <summary>
    /// 授予文件权限。
    /// </summary>
    /// <param name="fileId">文件 ID。</param>
    /// <param name="subjectType">主体类型（用户 / 角色 / 组织）。</param>
    /// <param name="subjectId">主体 ID。</param>
    /// <param name="permission">权限（可组合 flags）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task GrantAsync(string fileId, FileSubjectType subjectType, string subjectId, FilePermission permission, CancellationToken cancellationToken);

    /// <summary>
    /// 撤销文件权限。
    /// </summary>
    /// <param name="fileId">文件 ID。</param>
    /// <param name="subjectType">主体类型。</param>
    /// <param name="subjectId">主体 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RevokeAsync(string fileId, FileSubjectType subjectType, string subjectId, CancellationToken cancellationToken);

    /// <summary>
    /// 查询文件 ACL 条目。
    /// </summary>
    /// <param name="fileId">文件 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>ACL 条目列表。</returns>
    Task<IReadOnlyList<FileAclEntryInfo>> ListAclAsync(string fileId, CancellationToken cancellationToken);

    /// <summary>
    /// 检查指定主体是否有文件指定权限。
    /// </summary>
    /// <param name="fileId">文件 ID。</param>
    /// <param name="subjectType">主体类型。</param>
    /// <param name="subjectId">主体 ID。</param>
    /// <param name="permission">待检查权限。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>有返回 true。</returns>
    Task<bool> HasPermissionAsync(string fileId, FileSubjectType subjectType, string subjectId, FilePermission permission, CancellationToken cancellationToken);
}

/// <summary>
/// 文件权限（flags 枚举，可组合）。
/// </summary>
[Flags]
public enum FilePermission
{
    /// <summary>无权限。</summary>
    None = 0,

    /// <summary>读取。</summary>
    Read = 1,

    /// <summary>写入。</summary>
    Write = 2,

    /// <summary>删除。</summary>
    Delete = 4,

    /// <summary>管理权限（ACL 变更）。</summary>
    Manage = 8,

    /// <summary>全部权限。</summary>
    All = Read | Write | Delete | Manage
}

/// <summary>
/// ACL 主体类型。
/// </summary>
public enum FileSubjectType
{
    /// <summary>用户。</summary>
    User = 0,

    /// <summary>角色。</summary>
    Role = 1,

    /// <summary>组织。</summary>
    Org = 2
}

/// <summary>
/// 文件上传请求。
/// </summary>
public sealed record FileUploadRequest(
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? BizType = null,
    string? BizId = null);

/// <summary>
/// 文件引用（上传返回 / 下载定位）。
/// </summary>
public sealed record FileReference(
    string FileId,
    string StorageKey,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Hash,
    DateTimeOffset UploadedAt,
    string? BizType = null,
    string? BizId = null);

/// <summary>
/// ACL 条目信息。
/// </summary>
public sealed record FileAclEntryInfo(
    string EntryId,
    string FileId,
    FileSubjectType SubjectType,
    string SubjectId,
    FilePermission Permission,
    string GrantedBy,
    DateTimeOffset GrantedAt);
