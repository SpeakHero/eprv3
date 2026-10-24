/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 修改时间：2026-10-24
 * 描述：文件格式转换任务处理器（file.format.convert，ASYNC）：
 *       目标对象与目标格式前置校验；当前部署未启用转换引擎时显式失败（D-2），不静默成功
 */

using Microsoft.Extensions.Logging;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Jobs;
using Qlx.Modules.Support.Domain;

namespace Qlx.Modules.Support.Jobs.Files;

/// <summary>
/// 文件格式转换处理器（file.format.convert，ASYNC）。
/// <para>
/// 决策 D-2：本期未引入图像/文档转换引擎（ImageSharp / LibreOffice），因此任务必须
/// <b>显式失败</b>并给出可操作提示，禁止返回「成功但未转换」。前置校验（目标对象存在且为
/// 正常态文件、目标格式受支持）照常执行，保证调用方拿到的是真实校验结果而非桩文案。
/// </para>
/// <para>
/// 引入引擎后，只需在 <c>ExecuteAsync</c> 的引擎接入点实现转换并产出新版本，
/// 任务契约与调用方无需改动。
/// </para>
/// </summary>
/// <param name="db">租户上下文。</param>
/// <param name="logger">日志器。</param>
public sealed partial class FileFormatConvertHandler(
    SupportTenantDbContext db,
    ILogger<FileFormatConvertHandler> logger) : IJobHandler
{
    /// <summary>受支持的目标格式（引擎接入后生效）。</summary>
    private static readonly string[] s_supportedTargets = ["png", "jpg", "jpeg", "webp"];

    private readonly SupportTenantDbContext _db = db;
    private readonly ILogger<FileFormatConvertHandler> _logger = logger;

    /// <inheritdoc />
    public string TaskCode => FileTaskCodes.FormatConvert;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var targetFormat = FileJobHelpers.GetRequiredString(context, FileJobParams.TargetFormat)
            .TrimStart('.').ToLowerInvariant();
        if (!s_supportedTargets.Contains(targetFormat, StringComparer.Ordinal))
        {
            throw new ValidationException(FileJobParams.TargetFormat,
                $"不支持的目标格式 {targetFormat}（受支持：{string.Join('/', s_supportedTargets)}）");
        }

        var target = FileJobHelpers.RequireNormalTarget(
            await FileJobHelpers.LoadTargetAsync(_db, context, tracked: false, cancellationToken).ConfigureAwait(false),
            FileJobHelpers.ResolveResourceId(context));
        if (target.ObjectType != StorageObjectType.File)
        {
            throw new ValidationException(FileJobParams.FileId, "格式转换仅支持文件对象，目录对象不支持");
        }

        // D-2：本期未接入转换引擎（ImageSharp / 文档转换服务），显式失败而非静默成功。
        // 引擎接入后：在此执行转换、产出新版本（FileVersion）并清理临时产物。
        LogEngineMissing(target.Id, targetFormat, context.TaskCode);
        stopwatch.Stop();
        return new CompletedResult(
            null, null, null, stopwatch.ElapsedMilliseconds, IsSuccess: false,
            ErrorMessage: "当前部署未启用文件格式转换引擎（ImageSharp / 文档转换服务未配置），"
                + "file.format.convert 暂不可用；请先配置转换引擎后重新提交。");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "任务 {TaskCode} 无法执行：格式转换引擎未配置（对象 {ObjectId}，目标格式 {TargetFormat}）")]
    private partial void LogEngineMissing(long objectId, string targetFormat, string taskCode);
}
