/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 16:40:00 +08:00 中国标准时间 星期五
 * 描述：溯源二维码占位符渲染器——{{qrcode:内容}} → 内嵌 base64 PNG <img>（QRCoder 生成）
 */

using System.Text;
using System.Text.RegularExpressions;
using QRCoder;

namespace Qlx.BuildingBlocks.Printing;

/// <summary>
/// 溯源二维码占位符渲染器（公共组件，打印模板引擎随 BuildingBlocks 归口）。
/// <para>占位语法：双花括号包裹的 <c>qrcode:内容</c>（如 <c>{{qrcode:trace_code}}</c>）。
/// 其中「内容」优先按模板数据模型的字段名解析
/// （model 为 <c>IDictionary&lt;string, object?&gt;</c> 且包含该键时取键值，支持点号路径逐级下钻）；
/// 解析不到或 model 非字典时按字面文本处理。生成内容不含 HTML 转义（二维码面向扫码设备编码原始文本）。</para>
/// <para>渲染输出：内嵌 base64 PNG 的 <c>&lt;img&gt;</c> 标签（ECC 纠错级别 M，8 像素/模块）。</para>
/// </summary>
public static partial class QrCodePlaceholderRenderer
{
    /// <summary>二维码占位符模式（内容段允许除 } 外的任意字符；源生成预编译，200ms 匹配超时）。</summary>
    [GeneratedRegex(@"\{\{qrcode:([^}]*)\}\}", RegexOptions.CultureInvariant, 200)]
    private static partial Regex QrCodePlaceholderRegex();

    /// <summary>二维码容错级别（M：15% 纠错，兼顾密度与扫码稳健性）。</summary>
    private const QRCodeGenerator.ECCLevel EccLevel = QRCodeGenerator.ECCLevel.M;

    /// <summary>每个二维码模块的像素尺寸。</summary>
    private const int PixelsPerModule = 8;

    /// <summary>
    /// 扫描模板 HTML 中的全部 <c>{{"{{"}}qrcode:...}}</c> 占位符并替换为内嵌 base64 PNG 图片标签。
    /// </summary>
    /// <param name="html">原始模板 HTML。</param>
    /// <param name="model">模板数据模型（字典时支持按字段名 / 点号路径取值）。</param>
    /// <returns>替换后的 HTML。</returns>
    public static string Render(string html, object? model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);

        return QrCodePlaceholderRegex().Replace(html, match =>
        {
            var content = ResolveContent(match.Groups[1].Value.Trim(), model);
            return BuildQrImageTag(content);
        });
    }

    /// <summary>解析占位符内容：字典模型按字段名 / 点号路径取值，否则按字面文本。</summary>
    /// <param name="reference">占位符内容段。</param>
    /// <param name="model">模板数据模型。</param>
    /// <returns>二维码编码内容（空内容占位为空串，生成空图占位）。</returns>
    private static string ResolveContent(string reference, object? model)
    {
        if (reference.Length == 0)
        {
            return string.Empty;
        }

        if (model is not IReadOnlyDictionary<string, object?> dictionary)
        {
            return reference;
        }

        if (dictionary.TryGetValue(reference, out var direct) && direct is not null)
        {
            return direct.ToString() ?? string.Empty;
        }

        // 点号路径逐级下钻（仅支持字典层级，如 qrcode:order.trace_code）
        var segments = reference.Split('.');
        if (segments.Length > 1 && dictionary.TryGetValue(segments[0], out var root))
        {
            object? current = root;
            for (var i = 1; i < segments.Length && current is IReadOnlyDictionary<string, object?> level; i++)
            {
                current = level.GetValueOrDefault(segments[i]);
            }

            if (current is not null && current is not IReadOnlyDictionary<string, object?>)
            {
                return current.ToString() ?? string.Empty;
            }
        }

        return reference;
    }

    /// <summary>生成二维码 PNG 并包装为内嵌 base64 图片标签。</summary>
    /// <param name="content">二维码内容。</param>
    /// <returns>img 标签 HTML。</returns>
    private static string BuildQrImageTag(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, EccLevel);
        using var qrCode = new PngByteQRCode(data);
        var pngBytes = qrCode.GetGraphic(PixelsPerModule);

        var builder = new StringBuilder(pngBytes.Length * 4 / 3 + 64);
        builder.Append("<img src=\"data:image/png;base64,");
        builder.Append(Convert.ToBase64String(pngBytes));
        builder.Append("\" alt=\"qrcode\"/>");
        return builder.ToString();
    }
}
