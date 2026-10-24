/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：Outbox 存储契约，默认内存实现，生产经 EF Core 持久化实现替换
 */

namespace Qlx.BuildingBlocks.EventBus.Outbox;

/// <summary>
/// Outbox 存储。生产环境使用 EF Core 持久化实现（持久化轮询表 + 行锁租约），
/// 本契约同时被进程内内存实现用于开发与单元测试。
/// </summary>
public interface IOutboxStore
{
    /// <summary>
    /// 写入一条待投递消息。
    /// </summary>
    /// <param name="message">消息记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// 以行锁租约方式领取一批到期的未处理消息；
    /// 已被其它实例持有且租约未过期的消息不参与领取。
    /// </summary>
    /// <param name="owner">领取方实例标识。</param>
    /// <param name="maxCount">批量上限。</param>
    /// <param name="leaseDuration">租约时长。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本批领取的消息。</returns>
    Task<IReadOnlyList<OutboxMessage>> LeaseBatchAsync(
        string owner,
        int maxCount,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    /// <summary>
    /// 标记消息处理成功。仅租约持有者可操作。
    /// </summary>
    /// <param name="messageId">消息标识。</param>
    /// <param name="owner">租约持有者标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task CompleteAsync(long messageId, string owner, CancellationToken cancellationToken);

    /// <summary>
    /// 标记消息本次投递失败并延后重试。仅租约持有者可操作。
    /// </summary>
    /// <param name="messageId">消息标识。</param>
    /// <param name="owner">租约持有者标识。</param>
    /// <param name="failureReason">失败原因。</param>
    /// <param name="retryDelay">重试退避时长。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task FailAsync(long messageId, string owner, string failureReason, TimeSpan retryDelay, CancellationToken cancellationToken);
}
