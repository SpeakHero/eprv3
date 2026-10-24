/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 06:05:00 +08:00 中国标准时间 星期五
 * 描述：消息模板渲染器——${var} 占位符解析 + VarSchema 校验（必填/未知/类型）+ HTML 转义
 */

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Serialization;

namespace Qlx.Modules.Support.Services.Messages;

/// <summary>模板渲染结果。</summary>
/// <param name="Subject">渲染后标题。</param>
/// <param name="Content">渲染后正文。</param>
public sealed record TemplateRenderResult(string Subject, string Content);

/// <summary>变量模式条目（var_schema JSON 数组元素）。</summary>
/// <param name="Name">变量名。</param>
/// <param name="Type">类型（string / number / boolean；缺省 string）。</param>
/// <param name="Required">是否必填。</param>
public sealed record TemplateVarSchema(string Name, string? Type = null, bool Required = false);

/// <summary>
/// 模板渲染器。占位符语法 <c>${var}</c>；变量名限 <c>\w+</c>。
/// 校验次序：未知变量（模板含未注册占位符）→ 必填缺失 → 类型不符；全部通过后按转义规则插值。
/// </summary>
public static partial class TemplateRenderer
{
    /// <summary>标题 / 正文共用的占位符模式（源生成预编译，200ms 匹配超时）。</summary>
    [GeneratedRegex(@"\$\{(\w+)\}", RegexOptions.CultureInvariant, 200)]
    private static partial Regex PlaceholderRegex();

    /// <summary>
    /// 渲染模板。
    /// </summary>
    /// <param name="subjectTemplate">标题模板。</param>
    /// <param name="contentTemplate">正文模板。</param>
    /// <param name="varSchemaJson">变量模式 JSON（[{name,type,required}] 数组）。</param>
    /// <param name="variables">变量字典（可空）。</param>
    /// <param name="escapeHtml">是否对插值做 HTML 转义（邮件渠道 true，站内信展示端转义时可 false；默认 true）。</param>
    /// <returns>渲染结果。</returns>
    /// <exception cref="ValidationException">模板解析失败 / 未知变量 / 必填缺失 / 类型不符。</exception>
    public static TemplateRenderResult Render(
        string subjectTemplate,
        string contentTemplate,
        string varSchemaJson,
        IReadOnlyDictionary<string, object?>? variables,
        bool escapeHtml = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectTemplate);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentTemplate);

        var schema = ParseSchema(varSchemaJson);
        var schemaByName = schema.ToDictionary(static v => v.Name, static v => v, StringComparer.Ordinal);

        // ① 模板中出现的占位符必须全部在 var_schema 注册（防模板越权注入未审变量）
        var placeholders = CollectPlaceholders(subjectTemplate, contentTemplate);
        var unknown = placeholders.Where(p => !schemaByName.ContainsKey(p)).ToList();
        if (unknown.Count > 0)
        {
            throw new ValidationException(
                [new FieldError("template", $"模板包含未注册变量：{string.Join(", ", unknown)}")]);
        }

        // ② 必填变量必须提供
        var missing = schema
            .Where(v => v.Required && IsMissing(variables, v.Name))
            .Select(static v => v.Name)
            .ToList();
        if (missing.Count > 0)
        {
            throw new ValidationException(
                [new FieldError("variables", $"必填变量缺失：{string.Join(", ", missing)}")]);
        }

        // ③ 类型校验（number / boolean）
        var errors = new List<FieldError>();
        foreach (var varSchema in schema)
        {
            if (variables is null || !variables.TryGetValue(varSchema.Name, out var value) || value is null)
            {
                continue;
            }

            var invalid = varSchema.Type?.ToLowerInvariant() switch
            {
                "number" => value is not (byte or sbyte or short or ushort or int or uint or long or ulong
                    or float or double or decimal)
                    && !decimal.TryParse(value.ToString(), out _),
                "boolean" => value is not bool && !bool.TryParse(value.ToString(), out _),
                _ => false,
            };
            if (invalid)
            {
                errors.Add(new FieldError(varSchema.Name, $"变量 {varSchema.Name} 类型应为 {varSchema.Type}。"));
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return new TemplateRenderResult(
            RenderText(subjectTemplate, variables, escapeHtml),
            RenderText(contentTemplate, variables, escapeHtml));
    }

    /// <summary>解析变量模式 JSON；空串按空数组处理，非法结构抛校验异常。</summary>
    /// <param name="varSchemaJson">JSON 字符串。</param>
    /// <returns>变量模式列表。</returns>
    private static List<TemplateVarSchema> ParseSchema(string varSchemaJson)
    {
        if (string.IsNullOrWhiteSpace(varSchemaJson))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<TemplateVarSchema>>(varSchemaJson, QlxJsonDefaults.Default);
            return parsed ?? [];
        }
        catch (JsonException ex)
        {
            throw new ValidationException(
                [new FieldError("template", $"模板 var_schema 解析失败：{ex.Message}")]);
        }
    }

    /// <summary>判定必填变量是否缺失（未提供 / null / 空白字符串均视为缺失）。</summary>
    /// <param name="variables">变量字典（可空）。</param>
    /// <param name="name">变量名。</param>
    /// <returns>缺失返回 true。</returns>
    private static bool IsMissing(IReadOnlyDictionary<string, object?>? variables, string name)
    {
        if (variables is null || !variables.TryGetValue(name, out var provided) || provided is null)
        {
            return true;
        }

        return provided is string text && string.IsNullOrWhiteSpace(text);
    }

    /// <summary>收集模板中出现的全部占位符变量名（去重）。</summary>
    /// <param name="subjectTemplate">标题模板。</param>
    /// <param name="contentTemplate">正文模板。</param>
    /// <returns>变量名集合。</returns>
    private static IReadOnlyList<string> CollectPlaceholders(string subjectTemplate, string contentTemplate)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var match in PlaceholderRegex().Matches(subjectTemplate))
        {
            names.Add(((Match)match).Groups[1].Value);
        }

        foreach (var match in PlaceholderRegex().Matches(contentTemplate))
        {
            names.Add(((Match)match).Groups[1].Value);
        }

        return [.. names];
    }

    /// <summary>按占位符插值渲染单段文本。</summary>
    /// <param name="text">模板文本。</param>
    /// <param name="variables">变量字典。</param>
    /// <param name="escapeHtml">是否 HTML 转义。</param>
    /// <returns>渲染后文本。</returns>
    private static string RenderText(
        string text, IReadOnlyDictionary<string, object?>? variables, bool escapeHtml)
    {
        return PlaceholderRegex().Replace(text, match =>
        {
            var name = match.Groups[1].Value;
            if (variables is null || !variables.TryGetValue(name, out var value) || value is null)
            {
                return match.Value; // 未提供且非必填：保留原占位符（显式可见）
            }

            var rendered = escapeHtml ? EscapeHtml(value.ToString() ?? string.Empty) : value.ToString();
            return rendered ?? string.Empty;
        });
    }

    /// <summary>HTML 转义（&lt; &gt; &amp; &quot; 单引号）。</summary>
    /// <param name="value">原始文本。</param>
    /// <returns>转义后文本。</returns>
    private static string EscapeHtml(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(ch switch
            {
                '<' => "&lt;",
                '>' => "&gt;",
                '&' => "&amp;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => ch,
            });
        }

        return builder.ToString();
    }
}
