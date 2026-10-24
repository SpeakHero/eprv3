/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 17:10:00 +08:00 中国标准时间 星期五
 * 描述：Support 模块服务任务（ServiceJobs）清单声明测试——编码唯一性、与调度任务不重叠、
 *       声明模式与处理器注册形态约定
 */

using Qlx.Abstractions.Jobs;
using Qlx.Modules.Support;
using Xunit;

namespace Qlx.BuildingBlocks.Tests.Jobs;

/// <summary>
/// <see cref="SupportModule.Manifest"/> 服务任务声明测试（无数据库依赖，纯清单形态校验）。
/// 服务任务红线：CRON 恒空、NextRunAt 恒 null（调度扫描永不命中），仅经 SubmitAsync 按需执行。
/// </summary>
public sealed class SupportModuleServiceJobsTests
{
    [Fact]
    public void ServiceJobCodesAreUniqueAndDoNotOverlapScheduledJobs()
    {
        var manifest = new SupportModule().Manifest;
        var serviceCodes = manifest.ServiceJobs.Select(static j => j.JobCode).ToList();
        var scheduledCodes = manifest.Jobs.Select(static j => j.JobCode).ToList();

        Assert.Equal(serviceCodes.Count, serviceCodes.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(serviceCodes.Intersect(scheduledCodes, StringComparer.Ordinal));
    }

    [Fact]
    public void ServiceJobsCoverAllTaskifiedFileOperations()
    {
        var manifest = new SupportModule().Manifest;
        var codes = manifest.ServiceJobs.Select(static j => j.JobCode).ToHashSet(StringComparer.Ordinal);

        // 文件服务全部对外方法任务化（S18/D22 红线），缺一会致 SubmitAsync 404
        string[] expected =
        [
            FileTaskCodes.Upload, FileTaskCodes.Download, FileTaskCodes.Preview,
            FileTaskCodes.Metadata, FileTaskCodes.List, FileTaskCodes.Delete,
            FileTaskCodes.Restore, FileTaskCodes.RecycleList, FileTaskCodes.AclSet,
            ImportExportTaskCodes.Export, ImportExportTaskCodes.BatchImport,
        ];
        foreach (var code in expected)
        {
            Assert.Contains(code, codes);
        }
    }

    [Fact]
    public void ServiceJobsDeclareHandlerTypeAndMode()
    {
        var manifest = new SupportModule().Manifest;

        foreach (var job in manifest.ServiceJobs)
        {
            Assert.False(string.IsNullOrWhiteSpace(job.HandlerTypeName));
            // 导入导出为 ASYNC 按需触发，其余文件操作一律 SYNC 直执行
            var expectAsync = job.JobCode is ImportExportTaskCodes.Export or ImportExportTaskCodes.BatchImport;
            Assert.Equal(expectAsync ? ExecutionMode.Async : ExecutionMode.Sync, job.Mode);
        }
    }

    [Fact]
    public void StubTasksAreNotDeclaredAsServiceJobs()
    {
        // chunk.assemble / format.convert 为待文件中心批次的桩处理器，暂不声明避免幽灵定义
        var manifest = new SupportModule().Manifest;
        var codes = manifest.ServiceJobs.Select(static j => j.JobCode).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(FileTaskCodes.ChunkAssemble, codes);
        Assert.DoesNotContain(FileTaskCodes.FormatConvert, codes);
    }
}
