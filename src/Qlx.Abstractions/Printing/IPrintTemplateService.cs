/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:50:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:50:00 +08:00 中国标准时间 星期三
 * 描述：打印模板渲染 / PDF 渲染契约
 */

namespace Qlx.Abstractions.Printing;

/// <summary>
/// 打印模板服务接口。
/// </summary>
public interface IPrintTemplateService
{
    /// <summary>
    /// 渲染打印模板为 HTML。
    /// </summary>
    /// <param name="templateCode">模板编码。</param>
    /// <param name="model">模板数据模型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>渲染后的 HTML。</returns>
    Task<string> RenderAsync(string templateCode, object model, CancellationToken cancellationToken);

    /// <summary>
    /// 获取打印模板定义。
    /// </summary>
    /// <param name="templateCode">模板编码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>模板定义（未找到返回 null）。</returns>
    Task<PrintTemplateInfo?> GetTemplateAsync(string templateCode, CancellationToken cancellationToken);
}

/// <summary>
/// PDF 渲染接口（可替换实现，阶段二默认空实现）。
/// </summary>
public interface IPdfRenderer
{
    /// <summary>
    /// 将 HTML 渲染为 PDF。
    /// </summary>
    /// <param name="html">输入 HTML。</param>
    /// <param name="options">渲染选项（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>PDF 字节数组。</returns>
    Task<byte[]> RenderAsync(string html, PdfRenderOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// PDF 渲染选项。
/// </summary>
public sealed record PdfRenderOptions
{
    /// <summary>页面宽度（mm），默认 210（A4）。</summary>
    public int PageWidthMm { get; init; } = 210;

    /// <summary>页面高度（mm），默认 297（A4）。</summary>
    public int PageHeightMm { get; init; } = 297;

    /// <summary>页面边距（mm），默认 10。</summary>
    public int MarginMm { get; init; } = 10;
}

/// <summary>
/// 打印模板定义。
/// </summary>
public sealed record PrintTemplateInfo(
    string TemplateCode,
    string Name,
    string ModuleCode,
    string HtmlTemplate,
    string? Css = null);
