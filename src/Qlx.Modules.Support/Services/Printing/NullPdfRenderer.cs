/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 14:10:00 +08:00 中国标准时间 星期五
 * 描述：PDF 渲染默认空实现（阶段二占位，宿主注册真实引擎后整体替换）
 */

using Qlx.Abstractions.Printing;

namespace Qlx.Modules.Support.Services.Printing;

/// <summary>
/// <see cref="IPdfRenderer"/> 默认空实现。阶段二仅交付 HTML 渲染与打印模板管理（方案允许 PDF 引擎接口预留），
/// 调用即抛 <see cref="NotSupportedException"/> 指引接入；宿主引入真实引擎
/// （如 PuppeteerSharp / QuestPDF）后经 DI 注册整体替换，业务零改动。
/// </summary>
public sealed class NullPdfRenderer : IPdfRenderer
{
    /// <inheritdoc />
    public Task<byte[]> RenderAsync(string html, PdfRenderOptions? options, CancellationToken cancellationToken)
        => throw new NotSupportedException("PDF 渲染引擎尚未接入：阶段二仅提供 HTML 渲染与打印模板管理，PDF 输出待后续批次接入。");
}
