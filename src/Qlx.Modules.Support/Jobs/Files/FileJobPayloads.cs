/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 23:58:00 +08:00 中国标准时间 星期四
 * 描述：文件任务结果载荷 record（调度器 CompletedResult.ResultJson 承载，进程内传递真实 long Id）
 */

namespace Qlx.Modules.Support.Jobs.Files;

/// <summary>上传任务结果载荷（Deduplicated=true 表示秒传命中）。</summary>
public sealed record FileUploadJobPayload(
    long FileId,
    string StorageKey,
    string Hash,
    long SizeBytes,
    string Path,
    string BizDomain,
    bool Deduplicated,
    int RefCount,
    string? Warning = null);

/// <summary>文件元数据载荷。</summary>
public sealed record FileMetadataJobPayload(
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

/// <summary>下载/预览任务结果载荷（进程内流式传递）。Content 不参与 ResultJson 序列化落库（流不可序列化，且内容不应入 job_executions）。</summary>
public sealed record FileContentJobPayload(
    long FileId,
    [property: System.Text.Json.Serialization.JsonIgnore] Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes);

/// <summary>文件列表/回收站列表载荷。</summary>
public sealed record FileListJobPayload(IReadOnlyList<FileMetadataJobPayload> Items);

/// <summary>ACL 条目设置入参（进程内载荷）。</summary>
/// <param name="SubjectType">主体类型（0=用户 1=角色 2=组织）。</param>
/// <param name="SubjectId">主体 Id。</param>
/// <param name="Permission">权限位（int）。</param>
/// <param name="AceType">1=Allow 2=Deny。</param>
/// <param name="Propagate">是否向子级传播。</param>
public sealed record AclEntryPayload(int SubjectType, string SubjectId, int Permission, int AceType, bool Propagate);
