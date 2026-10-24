/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 16:40:00 +08:00 中国标准时间 星期五
 * 描述：Scriban HTML 模板渲染公共组件——qrcode 占位替换 + 数据占位渲染
 *       （RazorLight 在 .NET 10 运行时编译失败，按方案 R2 预案选型 Scriban；引擎随 BuildingBlocks 归口）
 */

using Qlx.Abstractions.Exceptions;
using Scriban;
using Scriban.Runtime;

namespace Qlx.BuildingBlocks.Printing;

/// <summary>
/// Scriban HTML 模板渲染公共组件（打印模板引擎，RazorLight 在 .NET 10 运行时编译失败按 R2 预案选型 Scriban）。
/// <para>渲染管线：qrcode 溯源二维码占位替换（<see cref="QrCodePlaceholderRenderer"/>）→ Scriban 数据占位渲染。
/// 数据占位语法 <c>{{ 模型字段 }}</c>；成员名保持 .NET 原名，与字典键一致；
/// 字典模型逐键写入全局作用域，非字典模型导入公共成员。</para>
/// </summary>
public static class ScribanHtmlTemplateRenderer
{
    /// <summary>模板语法错误业务错误码（错误码归口：打印模板引擎全局唯一实现）。</summary>
    private const string TemplateSyntaxErrorCode = "PRINT_TEMPLATE_SYNTAX";

    /// <summary>
    /// 渲染模板 HTML：先替换 qrcode 占位符，再执行 Scriban 数据占位渲染。
    /// </summary>
    /// <param name="html">模板 HTML（含 Scriban 数据占位符与 qrcode 占位符）。</param>
    /// <param name="model">模板数据模型（字典逐键写入，非字典导入公共成员）。</param>
    /// <returns>渲染后的 HTML（不含 CSS 并入，由调用方按需拼接）。</returns>
    /// <exception cref="ConflictException">模板 Scriban 语法错误（错误码 PRINT_TEMPLATE_SYNTAX）。</exception>
    public static async Task<string> RenderAsync(string html, object model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);
        ArgumentNullException.ThrowIfNull(model);

        // ① 二维码占位先替换（Scriban 无法解析 qrcode: 前缀语法，且 base64 输出不含 Scriban 语法）
        var htmlWithQr = QrCodePlaceholderRenderer.Render(html, model);

        // ② Scriban 数据占位渲染
        var template = Template.Parse(htmlWithQr);
        if (template.HasErrors)
        {
            throw new ConflictException(
                $"打印模板 HTML 语法错误：{string.Join("; ", template.Messages)}",
                TemplateSyntaxErrorCode);
        }

        var scriptObject = new ScriptObject();
        if (model is IDictionary<string, object?> dictionary)
        {
            foreach (var (key, value) in dictionary)
            {
                scriptObject[key] = value;
            }
        }
        else
        {
            // 非字典模型（匿名对象 / 实体）：导入公共成员（成员名保持原名）
            scriptObject.Import(model);
        }

        var context = new TemplateContext
        {
            MemberRenamer = static member => member.Name,
        };
        context.PushGlobal(scriptObject);
        return await template.RenderAsync(context).ConfigureAwait(false);
    }
}
