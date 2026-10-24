/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：workerId 租约持有器，宿主启动期填充，供雪花生成器工厂读取
 */

namespace Qlx.BuildingBlocks.Ids;

/// <summary>
/// workerId 租约持有器。由 <see cref="WorkerIdLeaseHostedService"/> 在宿主启动期填充。
/// </summary>
public interface IWorkerIdLeaseHolder
{
    /// <summary>
    /// 已分配的 workerId；未初始化为 <see langword="null"/>。
    /// </summary>
    long? WorkerId { get; set; }
}

/// <inheritdoc />
public sealed class WorkerIdLeaseHolder : IWorkerIdLeaseHolder
{
    /// <inheritdoc />
    public long? WorkerId { get; set; }
}
