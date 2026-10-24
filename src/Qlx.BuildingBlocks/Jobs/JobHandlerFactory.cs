/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 21:45:00 +08:00 中国标准时间 星期四
 * 描述：任务处理器工厂（按 TaskCode 分发 + 启动期全局唯一校验）
 */

using Microsoft.Extensions.DependencyInjection;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Jobs;

namespace Qlx.BuildingBlocks.Jobs;

/// <summary>
/// 任务处理器工厂契约：按 TaskCode 从当前作用域解析 <see cref="IJobHandler"/>。
/// </summary>
public interface IJobHandlerFactory
{
    /// <summary>
    /// 按任务编码解析处理器。
    /// </summary>
    /// <param name="taskCode">任务编码。</param>
    /// <returns>处理器实例。</returns>
    /// <exception cref="NotFoundException">未注册的 TaskCode（HTTP 语义 404）。</exception>
    IJobHandler Resolve(string taskCode);

    /// <summary>
    /// 启动期校验 TaskCode 全局唯一。存在重复注册即抛异常，宿主启动失败（快速暴露装配错误）。
    /// </summary>
    /// <exception cref="InvalidOperationException">存在重复 TaskCode。</exception>
    void ValidateUniqueTaskCodes();
}

/// <summary>
/// 任务处理器工厂默认实现。处理器注册为 Scoped（多租户场景），从当前请求 / Worker 作用域解析。
/// </summary>
public sealed class JobHandlerFactory(IServiceProvider provider) : IJobHandlerFactory
{
    private readonly IServiceProvider _provider = provider;

    /// <inheritdoc />
    public IJobHandler Resolve(string taskCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskCode);

        return _provider.GetServices<IJobHandler>().FirstOrDefault(h => h.TaskCode == taskCode)
            ?? throw new NotFoundException("任务处理器", taskCode);
    }

    /// <inheritdoc />
    public void ValidateUniqueTaskCodes()
    {
        var duplicates = _provider.GetServices<IJobHandler>()
            .GroupBy(h => h.TaskCode, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"任务编码全局唯一校验失败，以下 TaskCode 被重复注册：{string.Join(", ", duplicates)}");
        }
    }
}
