/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 05:30:00 +08:00 中国标准时间 星期五
 * 描述：SSE 事件路由信封——推送目标（租户/用户）封装在 SseEvent.DataJson 内，
 *       连接管理器解析信封执行租户路由；信封缺失即拒发（防跨租户串流）
 */

using System.Text.Json;
using Qlx.Abstractions.Serialization;

namespace Qlx.BuildingBlocks.Sse;

/// <summary>
/// SSE 事件路由信封。<see cref="Qlx.Abstractions.Sse.SseEvent"/> 契约不携带租户/用户路由信息，
/// 约定生产方将本信封序列化（<see cref="QlxJsonDefaults.Default"/>）为 <c>DataJson</c>，
/// 连接管理器解析信封决定投递范围后原样下发（客户端收到信封含主题/正文/业务引用摘要）。
/// </summary>
/// <param name="TenantId">目标租户 Id（必填；缺失或非正数的事件整条拒发）。</param>
/// <param name="RecipientUserId">目标用户 Id（null=租户内广播）。</param>
/// <param name="Subject">事件主题（消息标题摘要）。</param>
/// <param name="Content">事件正文摘要。</param>
/// <param name="BizType">业务引用类型（可空）。</param>
/// <param name="BizId">业务引用 Id（可空）。</param>
/// <param name="BizNo">业务引用单号（可空）。</param>
public sealed record SseEventEnvelope(
    long TenantId,
    long? RecipientUserId,
    string Subject,
    string Content,
    string? BizType = null,
    string? BizId = null,
    string? BizNo = null)
{
    /// <summary>
    /// 将信封序列化为 <c>DataJson</c> 载荷。
    /// </summary>
    /// <param name="envelope">信封。</param>
    /// <returns>JSON 字符串。</returns>
    public static string Serialize(SseEventEnvelope envelope)
        => JsonSerializer.Serialize(envelope, QlxJsonDefaults.Default);

    /// <summary>
    /// 解析 <c>DataJson</c> 为信封；载荷非法（非 JSON / 缺路由字段 / 租户 Id 非正数）返回 null。
    /// </summary>
    /// <param name="dataJson">事件载荷。</param>
    /// <returns>信封；不可路由时为 null。</returns>
    public static SseEventEnvelope? TryParse(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return default;
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<SseEventEnvelope>(dataJson, QlxJsonDefaults.Default);
            return envelope is not null && envelope.TenantId > 0 ? envelope : default;
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
