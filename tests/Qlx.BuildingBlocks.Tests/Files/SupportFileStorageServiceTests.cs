/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 03:55:00 +08:00 中国标准时间 星期五
 * 描述：文件存储门面服务单元测试——S18/D22 红线：验证全部对外方法只经 IJobScheduler.SubmitAsync（SYNC）提交，
 *       以及 R1 上传上限、任务失败/载荷缺失错误映射、Id 混淆输出
 */

using Microsoft.Extensions.Options;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Files;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Files;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Support;
using Qlx.Modules.Support.Jobs.Files;
using Qlx.Modules.Support.Options;
using Qlx.Modules.Support.Services.Files;

namespace Qlx.BuildingBlocks.Tests.Files;

/// <summary>捕获提交请求的调度器桩。</summary>
internal sealed class CapturingJobScheduler(Func<TaskSubmitRequest, TaskSubmitResponse> responder) : IJobScheduler
{
    public List<TaskSubmitRequest> Requests { get; } = [];

    public Task<TaskSubmitResponse> SubmitAsync(TaskSubmitRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(responder(request));
    }

    public Task CancelAsync(Guid executionId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<TaskSubmitResponse?> GetStatusAsync(Guid executionId, CancellationToken cancellationToken)
        => Task.FromResult<TaskSubmitResponse?>(null);
}

/// <summary>测试用租户上下文。</summary>
internal sealed class TestTenantContext(long? tenantId) : ITenantContext
{
    public string? TenantCode { get; private set; } = tenantId is > 0 ? $"t-{tenantId}" : null;

    public long? TenantId { get; private set; } = tenantId;

    public bool IsResolved => TenantId is > 0;

    public void SetTenantId(long tenantId) => TenantId = tenantId > 0 ? tenantId : null;

    public void SetTenantCode(string? tenantCode) => TenantCode = tenantCode;
}

/// <summary>测试用当前用户上下文。</summary>
internal sealed class TestCurrentUserContext(string? userId) : ICurrentUserContext
{
    public string? UserId { get; } = userId;

    public string? UserName { get; } = userId is null ? null : $"{userId}@test";

    public string? UserDisplayName { get; } = userId is null ? null : $"用户 {userId}";

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(UserId);
}

public sealed class SupportFileStorageServiceTests
{
    private const long TenantId = 1L;
    private const string Operator = "user-9";

    private static SupportFilesOptions BuildOptions()
        => new() { MaxSyncUploadBytes = 10 * 1024 * 1024 };

    /// <summary>默认响应：按任务码返回正确载荷类型（模拟处理器契约）。</summary>
    private static TaskSubmitResponse DefaultResponse(TaskSubmitRequest request)
    {
        object? payload = request.TaskCode switch
        {
            FileTaskCodes.Upload => new FileUploadJobPayload(
                1900000000000000000L, "k/ey", "hash", 4, "/purchase/2026/10/f.xlsx",
                "purchase", Deduplicated: false, RefCount: 1, Warning: null),
            FileTaskCodes.List or FileTaskCodes.RecycleList => new FileListJobPayload([]),
            _ => new FileMetadataJobPayload(
                42, "f.xlsx", "application/pdf", 4, "hash", "/purchase/2026/10/f.xlsx",
                "purchase", 1, Operator, "Normal", false, null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        };
        return new TaskSubmitResponse(
            Guid.NewGuid(), ExecutionMode.Sync, JobExecutionStatus.Success,
            DateTimeOffset.UtcNow, new CompletedResult(payload, null, null, 1, IsSuccess: true));
    }

    private static (SupportFileStorageService Service, CapturingJobScheduler Scheduler) Build(
        long? tenantId = TenantId, string? userId = Operator, SupportFilesOptions? options = null)
    {
        var scheduler = new CapturingJobScheduler(DefaultResponse);
        var service = new SupportFileStorageService(
            scheduler,
            new TestTenantContext(tenantId),
            new TestCurrentUserContext(userId),
            new HashidsObfuscator("test-salt", minLength: 8),
            Options.Create(options ?? BuildOptions()));
        return (service, scheduler);
    }

    private static TaskSubmitResponse FailedResponse(string message)
        => new(Guid.NewGuid(), ExecutionMode.Sync, JobExecutionStatus.Failed,
            DateTimeOffset.UtcNow, new CompletedResult(null, null, null, 0, IsSuccess: false, message));

    [Fact]
    public async Task UploadAsyncSubmitsSyncUploadTaskWithParameters()
    {
        var (service, scheduler) = Build();
        await using var content = new MemoryStream([1, 2, 3, 4]);

        var result = await service.UploadAsync(
            new FileUploadRequest(content, "报价单.xlsx", "application/vnd.ms-excel", 4, BizType: "pur"),
            bizModule: "pur", docType: "quote", TestContext.Current.CancellationToken);

        var request = Assert.Single(scheduler.Requests);
        Assert.Equal(FileTaskCodes.Upload, request.TaskCode);
        Assert.Equal(ExecutionMode.Sync, request.Mode);
        Assert.Equal(Operator, request.InitiatorUserId);
        Assert.Equal(GuidIdMapper.ToGuid(TenantId), request.TenantId);
        Assert.Equal("报价单.xlsx", request.Parameters![FileJobParams.FileName]);
        Assert.Equal("pur", request.Parameters[FileJobParams.BizModule]);
        Assert.Equal("quote", request.Parameters[FileJobParams.DocType]);
        Assert.Equal(4L, request.Parameters[FileJobParams.SizeBytes]);
        Assert.Equal(Operator, request.Parameters[FileJobParams.OperatorId]);
        Assert.False(result.Deduplicated);
    }

    [Fact]
    public async Task UploadAsyncRejectsOversizedFileWithoutSubmitting()
    {
        var (service, scheduler) = Build(options: new SupportFilesOptions { MaxSyncUploadBytes = 4 });

        await Assert.ThrowsAsync<ValidationException>(() => service.UploadAsync(
            new FileUploadRequest(Stream.Null, "big.bin", "application/octet-stream", 5),
            null, null, TestContext.Current.CancellationToken));
        Assert.Empty(scheduler.Requests);
    }

    [Fact]
    public async Task MissingTenantContextThrowsUnauthorized()
    {
        var (service, scheduler) = Build(tenantId: null);

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.GetMetadataAsync(42, TestContext.Current.CancellationToken));
        Assert.Empty(scheduler.Requests);
    }

    [Fact]
    public async Task UnauthenticatedUserThrowsUnauthorized()
    {
        var (service, scheduler) = Build(userId: null);

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.GetMetadataAsync(42, TestContext.Current.CancellationToken));
        Assert.Empty(scheduler.Requests);
    }

    [Fact]
    public async Task FailedTaskMapsToConflictException()
    {
        var scheduler = new CapturingJobScheduler(_ => FailedResponse("无读取权限"));
        var service = new SupportFileStorageService(
            scheduler, new TestTenantContext(TenantId), new TestCurrentUserContext(Operator),
            new HashidsObfuscator("test-salt", minLength: 8), Options.Create(BuildOptions()));

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            service.GetMetadataAsync(42, TestContext.Current.CancellationToken));
        Assert.Equal("FILE_TASK_FAILED", exception.ErrorCode);
    }

    [Fact]
    public async Task MismatchedPayloadMapsToPayloadError()
    {
        // file.metadata 成功但载荷类型不符（误回 FileListJobPayload）
        var scheduler = new CapturingJobScheduler(_ => new TaskSubmitResponse(
            Guid.NewGuid(), ExecutionMode.Sync, JobExecutionStatus.Success,
            DateTimeOffset.UtcNow,
            new CompletedResult(new FileListJobPayload([]), null, null, 1, IsSuccess: true)));
        var service = new SupportFileStorageService(
            scheduler, new TestTenantContext(TenantId), new TestCurrentUserContext(Operator),
            new HashidsObfuscator("test-salt", minLength: 8), Options.Create(BuildOptions()));

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            service.GetMetadataAsync(42, TestContext.Current.CancellationToken));
        Assert.Equal("FILE_TASK_PAYLOAD", exception.ErrorCode);
    }

    [Fact]
    public async Task GetMetadataSubmitsMetadataTaskWithFileIdAndOperator()
    {
        var (service, scheduler) = Build();

        await service.GetMetadataAsync(42, TestContext.Current.CancellationToken);

        var request = Assert.Single(scheduler.Requests);
        Assert.Equal(FileTaskCodes.Metadata, request.TaskCode);
        Assert.Equal(42L, request.Parameters![FileJobParams.FileId]);
        Assert.Equal(Operator, request.Parameters[FileJobParams.OperatorId]);
    }

    [Fact]
    public async Task OpenDownloadReturnsContentPayload()
    {
        await using var stream = new MemoryStream([9, 8, 7]);
        var scheduler = new CapturingJobScheduler(_ => new TaskSubmitResponse(
            Guid.NewGuid(), ExecutionMode.Sync, JobExecutionStatus.Success,
            DateTimeOffset.UtcNow,
            new CompletedResult(
                new FileContentJobPayload(42, stream, "附件.pdf", "application/pdf", 3),
                null, null, 1, IsSuccess: true)));
        var service = new SupportFileStorageService(
            scheduler, new TestTenantContext(TenantId), new TestCurrentUserContext(Operator),
            new HashidsObfuscator("test-salt", minLength: 8), Options.Create(BuildOptions()));

        var content = await service.OpenDownloadAsync(42, TestContext.Current.CancellationToken);

        Assert.Equal(42, content.FileId);
        Assert.Equal("附件.pdf", content.FileName);
        Assert.Equal("application/pdf", content.ContentType);
        Assert.Equal(3, content.SizeBytes);
        Assert.Same(stream, content.Content);
    }

    [Fact]
    public async Task ListRecycleSubmitsRecycleListTask()
    {
        var (service, scheduler) = Build();

        await service.ListRecycleAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(scheduler.Requests);
        Assert.Equal(FileTaskCodes.RecycleList, request.TaskCode);
        Assert.Equal(Operator, request.Parameters![FileJobParams.OperatorId]);
    }

    [Fact]
    public async Task DeleteByStorageKeySubmitsDeleteTask()
    {
        var (service, scheduler) = Build();

        await ((IFileStorageService)service).DeleteAsync("ab/cd/ef", TestContext.Current.CancellationToken);

        var request = Assert.Single(scheduler.Requests);
        Assert.Equal(FileTaskCodes.Delete, request.TaskCode);
        Assert.Equal("ab/cd/ef", request.Parameters![FileJobParams.StorageKey]);
        Assert.Equal(Operator, request.Parameters[FileJobParams.OperatorId]);
    }

    [Fact]
    public async Task ContractUploadObfuscatesFileId()
    {
        var fileId = 1900000000000000000L;
        var scheduler = new CapturingJobScheduler(_ => new TaskSubmitResponse(
            Guid.NewGuid(), ExecutionMode.Sync, JobExecutionStatus.Success,
            DateTimeOffset.UtcNow,
            new CompletedResult(
                new FileUploadJobPayload(fileId, "k/ey", "hash", 4, "/purchase/2026/10/报价单.xlsx",
                    "purchase", Deduplicated: false, RefCount: 1, Warning: null),
                null, null, 1, IsSuccess: true)));
        var service = new SupportFileStorageService(
            scheduler, new TestTenantContext(TenantId), new TestCurrentUserContext(Operator),
            new HashidsObfuscator("test-salt", minLength: 8), Options.Create(BuildOptions()));

        var reference = await ((IFileStorageService)service).UploadAsync(
            new FileUploadRequest(Stream.Null, "报价单.xlsx", "application/pdf", 4, BizType: "pur"),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(fileId.ToString(System.Globalization.CultureInfo.InvariantCulture), reference.FileId);
        Assert.Equal("pur", reference.BizType);
        Assert.True(reference.UploadedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task TemporaryUrlPointsToAuthorizedDownload()
    {
        var (service, scheduler) = Build();

        var url = await ((IFileStorageService)service).GetTemporaryUrlAsync(
            "ab/cd/ef", TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);

        Assert.StartsWith("/api/support/files/download/", url, StringComparison.Ordinal);
        Assert.Equal(FileTaskCodes.Metadata, Assert.Single(scheduler.Requests).TaskCode);
    }
}
