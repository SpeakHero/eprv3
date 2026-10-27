/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 响应格式枚举
 */

namespace Qlx.Abstractions.Ai;

/// <summary>
/// AI 响应格式。
/// </summary>
public enum AiResponseFormat
{
    /// <summary>纯文本。</summary>
    Text = 0,

    /// <summary>JSON（内容必须是合法 JSON 对象）。</summary>
    Json = 1,

    /// <summary>HTML（内容必须是合法 HTML 文档）。</summary>
    Html = 2,
    /// <summary>
    /// PDF（内容必须是合法 PDF 文档）。
    /// </summary>
    Pdf = 3,
    /// <summary>
    /// Markdown（内容必须是合法 Markdown 文档）。
    /// </summary>
    Markdown = 4,
    /// <summary>
    /// XML（内容必须是合法 XML 文档）。
    /// </summary>
    Xml = 5,
}
