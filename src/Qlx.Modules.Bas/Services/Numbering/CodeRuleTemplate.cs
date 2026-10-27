/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 00:05:00 +08:00 中国标准时间 星期一
 * 描述：编码规则分段模板解析（唯一入口）——{type} / {parent} / {yyyyMM} / {seq:N} 或字面文本，
 *       字面文本不得含大括号；非法模板由发号器包装为 422 校验异常
 */

using System.Globalization;
using System.Text.Json;

namespace Qlx.Modules.Bas.Services.Numbering;

/// <summary>
/// 编码规则分段模板解析器。发号器（<c>BasCodeNumberGenerator</c>）与编码规则页共用同一语法判定，
/// 禁止两处各写一套解析规则（避免校验通过但渲染失败）。
/// </summary>
internal static class CodeRuleTemplate
{
    /// <summary>类型代码段。</summary>
    internal const string TypeToken = "{type}";

    /// <summary>上级编码段。</summary>
    internal const string ParentToken = "{parent}";

    /// <summary>UTC 年月段。</summary>
    internal const string YearMonthToken = "{yyyyMM}";

    /// <summary>流水号段前缀（<c>{seq:N}</c>）。</summary>
    internal const string SequencePrefix = "{seq:";

    /// <summary>
    /// 解析分段模板 JSON 字符串数组并逐段校验语法。
    /// </summary>
    /// <param name="segmentsJson">分段模板 JSON（如 <c>["{type}","-","{seq:2}"]</c>）。</param>
    /// <param name="segments">解析成功后的分段数组。</param>
    /// <param name="error">解析失败原因（成功为 null）。</param>
    /// <returns>语法合法返回 <see langword="true"/>。</returns>
    public static bool TryParse(string? segmentsJson, out string[] segments, out string? error)
    {
        segments = [];
        error = null;

        if (string.IsNullOrWhiteSpace(segmentsJson))
        {
            error = "编码规则分段模板不能为空";
            return false;
        }

        string[]? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<string[]>(segmentsJson);
        }
        catch (JsonException)
        {
            error = $"编码规则分段模板非法（须为 JSON 字符串数组）：{segmentsJson}";
            return false;
        }

        if (parsed is null || parsed.Length == 0)
        {
            error = "编码规则分段模板至少需要一个分段";
            return false;
        }

        foreach (string? segment in parsed)
        {
            if (segment is null)
            {
                error = "编码规则分段不能为 null";
                return false;
            }

            if (segment is TypeToken or ParentToken or YearMonthToken
                || TryParseSequenceDigits(segment, out _))
            {
                continue;
            }

            if (segment.Contains('{', StringComparison.Ordinal)
                || segment.Contains('}', StringComparison.Ordinal))
            {
                error = $"编码规则分段非法：{segment}（仅支持 {{type}} / {{parent}} / {{yyyyMM}} / {{seq:N}} 或字面文本）";
                return false;
            }
        }

        segments = parsed;
        return true;
    }

    /// <summary>识别 <c>{seq:N}</c> 段并返回补零位数（N &gt; 0）。</summary>
    /// <param name="segment">分段文本。</param>
    /// <param name="digits">补零位数。</param>
    /// <returns>是流水号段返回 <see langword="true"/>。</returns>
    public static bool TryParseSequenceDigits(string segment, out int digits)
    {
        digits = 0;
        return segment.StartsWith(SequencePrefix, StringComparison.Ordinal)
               && segment.EndsWith('}')
               && int.TryParse(
                   segment.AsSpan(SequencePrefix.Length, segment.Length - SequencePrefix.Length - 1),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out digits)
               && digits > 0;
    }
}
