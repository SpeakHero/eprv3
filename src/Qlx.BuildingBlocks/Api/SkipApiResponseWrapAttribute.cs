/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-27 10:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-27 10:30:00 +08:00 中国标准时间 星期二
 * 描述：响应包装豁免标记——标注后 ApiResponseWrapperMiddleware 对该端点直通（SSE / 文件流等非 JSON 响应专用）
 */

namespace Qlx.BuildingBlocks.Api;

/// <summary>
/// 标记端点不参与 <see cref="ApiResponseWrapperMiddleware"/> 的统一响应包装。
/// 仅用于响应体非 JSON 的端点：SSE 事件流（text/event-stream）、文件下载 / 预览（FileStreamResult）等；
/// 标注后中间件不缓冲、不改写该端点响应。普通 JSON API 禁止标注（会破坏统一响应契约）。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class SkipApiResponseWrapAttribute : Attribute
{
}
