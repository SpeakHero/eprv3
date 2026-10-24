/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：workerId 租约契约，多实例部署时经 Redis 分配唯一工作节点
 */

namespace Qlx.BuildingBlocks.Ids;

/// <summary>
/// workerId 租约。多实例部署必须经 Redis 租约分配唯一工作节点，防止雪花 ID 冲突；
/// 单实例或开发环境可使用静态配置实现。
/// </summary>
public interface IWorkerIdLease : IAsyncDisposable
{
    /// <summary>
    /// 获取租约并返回分配到的 workerId；租约持有期间由实现负责自动续期。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分配到的 workerId（0-1023）。</returns>
    /// <exception cref="InvalidOperationException">无可用 workerId 时抛出。</exception>
    Task<long> AcquireAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 当前已分配的 workerId；未获取租约前为 <see langword="null"/>。
    /// </summary>
    long? WorkerId { get; }
}
