/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-21 02:00:00 +08:00 中国标准时间 星期三
 * 描述：Outbox 调度配置项，控制轮询间隔、批量、租约与重试策略
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.BuildingBlocks.EventBus.Outbox;

/// <summary>
/// Outbox 调度配置项。对应配置节 <c>Qlx:EventBus:Outbox</c>。
/// </summary>
public sealed record OutboxOptions
{
    /// <summary>
    /// 配置节名。
    /// </summary>
    public const string SectionName = "Qlx:EventBus:Outbox";

    /// <summary>
    /// 是否启用后台调度器；测试宿主可关闭。
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// 轮询间隔（毫秒）。
    /// </summary>
    [Range(100, 60000)]
    public int PollIntervalMs { get; init; } = 500;

    /// <summary>
    /// 单批领取条数上限。
    /// </summary>
    [Range(1, 500)]
    public int BatchSize { get; init; } = 50;

    /// <summary>
    /// 租约时长（秒）；实例崩溃后其它实例在此时间后接管。
    /// </summary>
    [Range(5, 3600)]
    public int LeaseSeconds { get; init; } = 30;

    /// <summary>
    /// 最大投递尝试次数；超过后消息停留待人工介入。
    /// </summary>
    [Range(1, 100)]
    public int MaxAttempts { get; init; } = 10;

    /// <summary>
    /// 失败重试退避基数（秒）；第 n 次重试延迟为 基数 × n。
    /// </summary>
    [Range(1, 3600)]
    public int RetryDelaySeconds { get; init; } = 5;
}
