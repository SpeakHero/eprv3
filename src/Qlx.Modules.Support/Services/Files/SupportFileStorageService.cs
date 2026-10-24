/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 01:45:00 +08:00 中国标准时间 星期五
 * 描述：文件存储门面服务实现（IFileStorageService + ISupportFileService）：全部对外方法只经 IJobScheduler.SubmitAsync（S18/D22 红线）
 */

using Microsoft.Extensions.Options;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Files;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Files;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Support.Jobs.Files;
using Qlx.Modules.Support.Options;

namespace Qlx.Modules.Support.Services.Files;

/// <summary>
/// 文件存储门面服务实现。所有文件操作（上传/下载/预览/元数据/列表/软删/还原/回收站/临时链接）
/// 统一封装为 <see cref="TaskSubmitRequest"/> 提交进程内调度器（SYNC），业务异常由处理器抛出并透传。
/// </summary>
public sealed class SupportFileStorageService(
    IJobScheduler scheduler,
    ITenantContext tenantContext,
    ICurrentUserContext currentUser,
    IIdObfuscator idObfuscator,
    IOptions<SupportFilesOptions> filesOptions) : IFileStorageService, ISupportFileService
{
    private readonly IJobScheduler _scheduler = scheduler;
    private readonly ITenantContext _tenantContext = tenantContext;
    private readonly ICurrentUserContext _currentUser = currentUser;
    private readonly IIdObfuscator _idObfuscator = idObfuscator;
    private readonly SupportFilesOptions _filesOptions = filesOptions.Value;

    /// <summary>当前租户 Id（缺失租户上下文即拒绝）。</summary>
    private long TenantId => _tenantContext.TenantId is > 0
        ? _tenantContext.TenantId.Value
        : throw new UnauthorizedException("缺少租户上下文");

    /// <summary>当前操作人标识（未认证即拒绝）。</summary>
    private string OperatorId => string.IsNullOrWhiteSpace(_currentUser.UserId)
        ? throw new UnauthorizedException()
        : _currentUser.UserId;

    /// <inheritdoc />
    public async Task<SupportFileUploadResult> UploadAsync(FileUploadRequest request, string? bizModule, string? docType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SizeBytes > _filesOptions.MaxSyncUploadBytes)
        {
            // 规则 R1：超过同步上传上限走 ASYNC 分片（2C 分片通道落地前明确拒绝）
            throw new ValidationException(nameof(request.SizeBytes),
                $"文件超过同步上传上限（{_filesOptions.MaxSyncUploadBytes} 字节），请使用分片上传（2C 提供）。");
        }

        var payload = await ExecuteAsync<FileUploadJobPayload>(FileTaskCodes.Upload, new Dictionary<string, object?>
        {
            [FileJobParams.FileName] = request.FileName,
            [FileJobParams.ContentType] = request.ContentType,
            [FileJobParams.SizeBytes] = request.SizeBytes,
            [FileJobParams.BizModule] = bizModule ?? request.BizType,
            [FileJobParams.DocType] = docType,
            [FileJobParams.BizId] = request.BizId,
            [FileJobParams.OperatorId] = OperatorId,
        }, cancellationToken,
        // 内容流不可 JSON 序列化：走运行期参数（仅内存传递，SYNC 专属）
        new Dictionary<string, object?> { [FileJobParams.Content] = request.Content }).ConfigureAwait(false);

        return new SupportFileUploadResult(payload.FileId, payload.StorageKey, System.IO.Path.GetFileName(payload.Path),
            request.ContentType, payload.SizeBytes, payload.Hash, payload.Path, payload.BizDomain,
            payload.Deduplicated, payload.Warning);
    }

    /// <inheritdoc />
    public Task<SupportFileMetadata> GetMetadataAsync(long fileId, CancellationToken cancellationToken)
        => ExecuteMetadataAsync(FileTaskCodes.Metadata, BuildFileParams(fileId), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupportFileMetadata>> ListAsync(long? parentId, string? keyword, CancellationToken cancellationToken)
    {
        var payload = await ExecuteAsync<FileListJobPayload>(FileTaskCodes.List, new Dictionary<string, object?>
        {
            [FileJobParams.ParentId] = parentId ?? 0,
            [FileJobParams.Keyword] = keyword,
            [FileJobParams.OperatorId] = OperatorId,
        }, cancellationToken).ConfigureAwait(false);
        return payload.Items.Select(ToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<SupportFileContent> OpenDownloadAsync(long fileId, CancellationToken cancellationToken)
        => await OpenContentAsync(FileTaskCodes.Download, fileId, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<SupportFileContent> OpenPreviewAsync(long fileId, CancellationToken cancellationToken)
        => await OpenContentAsync(FileTaskCodes.Preview, fileId, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<SupportFileMetadata> DeleteAsync(long fileId, CancellationToken cancellationToken)
        => ExecuteMetadataAsync(FileTaskCodes.Delete, BuildFileParams(fileId), cancellationToken);

    /// <inheritdoc />
    public Task<SupportFileMetadata> RestoreAsync(long fileId, CancellationToken cancellationToken)
        => ExecuteMetadataAsync(FileTaskCodes.Restore, BuildFileParams(fileId), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupportFileMetadata>> ListRecycleAsync(CancellationToken cancellationToken)
    {
        var payload = await ExecuteAsync<FileListJobPayload>(FileTaskCodes.RecycleList, new Dictionary<string, object?>
        {
            [FileJobParams.OperatorId] = OperatorId,
        }, cancellationToken).ConfigureAwait(false);
        return payload.Items.Select(ToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(string storageKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        var payload = await ExecuteAsync<FileContentJobPayload>(FileTaskCodes.Download, new Dictionary<string, object?>
        {
            [FileJobParams.StorageKey] = storageKey,
            [FileJobParams.OperatorId] = OperatorId,
        }, cancellationToken).ConfigureAwait(false);
        return payload.Content;
    }

    /// <summary>IFileStorageService 上传（2 参数契约形态）：走同一 file.upload 任务，FileId 混淆输出。</summary>
    async Task<FileReference> IFileStorageService.UploadAsync(FileUploadRequest request, CancellationToken cancellationToken)
    {
        var result = await UploadAsync(request, request.BizType, null, cancellationToken).ConfigureAwait(false);
        return new FileReference(
            _idObfuscator.Encode(result.FileId), result.StorageKey, result.FileName, result.ContentType,
            result.SizeBytes, result.Hash, DateTimeOffset.UtcNow, request.BizType, request.BizId);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        await ExecuteAsync<FileMetadataJobPayload>(FileTaskCodes.Delete, new Dictionary<string, object?>
        {
            [FileJobParams.StorageKey] = storageKey,
            [FileJobParams.OperatorId] = OperatorId,
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> GetTemporaryUrlAsync(string storageKey, TimeSpan expiration, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        // 2B 临时实现：返回走鉴权下载接口的相对 URL（expiration 仅预留；2C 换签名直链）
        var payload = await ExecuteAsync<FileMetadataJobPayload>(FileTaskCodes.Metadata, new Dictionary<string, object?>
        {
            [FileJobParams.StorageKey] = storageKey,
            [FileJobParams.OperatorId] = OperatorId,
        }, cancellationToken).ConfigureAwait(false);
        return $"/api/support/files/download/{_idObfuscator.Encode(payload.FileId)}";
    }

    /// <summary>按 fileId 构建通用参数（处理器要求 OperatorId 参与 ACL 判定）。</summary>
    private Dictionary<string, object?> BuildFileParams(long fileId)
        => new() { [FileJobParams.FileId] = fileId, [FileJobParams.OperatorId] = OperatorId };

    /// <summary>执行元数据/生命周期类任务并转换为 DTO。</summary>
    private async Task<SupportFileMetadata> ExecuteMetadataAsync(string taskCode, Dictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        var payload = await ExecuteAsync<FileMetadataJobPayload>(taskCode, parameters, cancellationToken).ConfigureAwait(false);
        return ToDto(payload);
    }

    /// <summary>打开下载/预览内容流。</summary>
    private async Task<SupportFileContent> OpenContentAsync(string taskCode, long fileId, CancellationToken cancellationToken)
    {
        var payload = await ExecuteAsync<FileContentJobPayload>(taskCode, BuildFileParams(fileId), cancellationToken).ConfigureAwait(false);
        return new SupportFileContent(payload.FileId, payload.Content, payload.FileName, payload.ContentType, payload.SizeBytes);
    }

    /// <summary>提交 SYNC 任务并断言成功，返回强类型载荷。</summary>
    private async Task<T> ExecuteAsync<T>(string taskCode, Dictionary<string, object?> parameters, CancellationToken cancellationToken,
        Dictionary<string, object?>? runtimeParameters = null)
        where T : class
    {
        var response = await _scheduler
            .SubmitAsync(new TaskSubmitRequest(
                taskCode,
                GuidIdMapper.ToGuid(TenantId),
                ExecutionMode.Sync,
                InitiatorUserId: _currentUser.UserId,
                Parameters: parameters)
            {
                RuntimeParameters = runtimeParameters,
            }, cancellationToken)
            .ConfigureAwait(false);

        if (response.CompletedResult is not { IsSuccess: true })
        {
            throw new ConflictException(
                response.CompletedResult?.ErrorMessage ?? $"文件任务 {taskCode} 执行失败（{response.InitialStatus}）。",
                "FILE_TASK_FAILED");
        }

        return response.CompletedResult.ResultJson as T
            ?? throw new ConflictException($"文件任务 {taskCode} 结果载荷缺失。", "FILE_TASK_PAYLOAD");
    }

    /// <summary>任务载荷 → 对外 DTO。</summary>
    private static SupportFileMetadata ToDto(FileMetadataJobPayload payload)
        => new(payload.FileId, payload.FileName, payload.ContentType, payload.SizeBytes, payload.Hash,
            payload.Path, payload.BizDomain, payload.CurrentVersion, payload.OwnerId, payload.Status,
            payload.IsProtected, payload.RecycledAt, payload.CreatedAt, payload.UpdatedAt);
}
