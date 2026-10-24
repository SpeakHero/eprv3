/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 01:30:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 02:40:00 +08:00 中国标准时间 星期五
 * 描述：2B 占位任务处理器（ASYNC 桩，2C 实现真实逻辑）：分片组装 / 格式转换。
 *       回收站清理与临时区清理已由 2C 真实实现（见 FilePurgeHandlers.cs），桩已移除
 */

using Microsoft.Extensions.Logging;
using Qlx.Abstractions.Jobs;

namespace Qlx.Modules.Support.Jobs.Files;

/// <summary>
/// 2B 占位桩基类：不抛异常、不落库，写警告日志后返回明确的失败结果。
/// ASYNC 提交方（2C 任务中心）依 InitialStatus=Failed 感知未实现；2B 内 SYNC 误提交也会得到可读错误。
/// </summary>
public abstract partial class FileStubHandlerBase(ILogger logger) : IJobHandler
{
    private readonly ILogger _logger = logger;

    /// <inheritdoc />
    public abstract string TaskCode { get; }

    /// <summary>桩对应的 2C 能力名称（日志与错误信息展示）。</summary>
    protected abstract string CapabilityName { get; }

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        LogStubWarning(context.TaskCode, CapabilityName);
        await Task.CompletedTask.ConfigureAwait(false);
        return new CompletedResult(
            null, null, null, ExecutionDurationMs: 0, IsSuccess: false,
            ErrorMessage: $"能力 {CapabilityName}（{context.TaskCode}）将在 2C 后台任务中心落地后提供，当前为占位桩。");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "任务 {TaskCode}（{Capability}）为 2B 占位桩，尚未实现，2C 后台任务中心落地后启用。")]
    private partial void LogStubWarning(string taskCode, string capability);
}

/// <summary>大文件分片组装桩（file.chunk.assemble，2C 实现）。</summary>
public sealed class FileChunkAssembleHandler(ILogger<FileChunkAssembleHandler> logger)
    : FileStubHandlerBase(logger)
{
    /// <inheritdoc />
    public override string TaskCode => FileTaskCodes.ChunkAssemble;

    /// <inheritdoc />
    protected override string CapabilityName => "大文件分片组装";
}

/// <summary>格式转换桩（file.format.convert，2C 实现）。</summary>
public sealed class FileFormatConvertHandler(ILogger<FileFormatConvertHandler> logger)
    : FileStubHandlerBase(logger)
{
    /// <inheritdoc />
    public override string TaskCode => FileTaskCodes.FormatConvert;

    /// <inheritdoc />
    protected override string CapabilityName => "文件格式转换";
}
