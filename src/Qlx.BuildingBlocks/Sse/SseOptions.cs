/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 05:30:00 +08:00 中国标准时间 星期五
 * 描述：SSE 基座强类型 Options（Qlx:Sse 节）：心跳周期 / 租户事件环形缓冲容量 / 连接通道容量
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.BuildingBlocks.Sse;

/// <summary>
/// SSE 基座配置。对应配置节 <c>Qlx:Sse</c>，启动期 ValidateOnStart 校验。
/// </summary>
public sealed record QlxSseOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Qlx:Sse";

    /// <summary>服务端心跳周期（秒），默认 30（规范 §信息中心：心跳 30s 保活）。</summary>
    [Range(5, 600)]
    public int HeartbeatSeconds { get; init; } = 30;

    /// <summary>每租户事件环形缓冲容量（条），默认 1000；断线续传仅覆盖窗口内事件。</summary>
    [Range(10, 100_000)]
    public int BufferCapacity { get; init; } = 1000;

    /// <summary>每连接事件通道容量（条），默认 64；满载淘汰最旧（客户端经 Last-Event-ID 续传补漏）。</summary>
    [Range(4, 10_000)]
    public int ChannelCapacity { get; init; } = 64;
}
