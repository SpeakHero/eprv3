/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 00:15:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 00:15:00 +08:00 中国标准时间 星期三
 * 描述：人民币金额中文大写与阿拉伯数字双向转换静态工具，全部匹配与转换逻辑基于预编译正则实现
 */

using System.Globalization;
using System.Text.RegularExpressions;

namespace Qlx.BuildingBlocks.Utilities;

/// <summary>
/// 中文大写金额 ↔ 阿拉伯数字金额 双向转换器（静态无状态工具，线程安全）
/// </summary>
/// <remarks>
/// <para>转换依据 GB/T 人民币大写规范：零壹贰叁肆伍陆柒捌玖、拾佰仟、万亿兆京垓秭穰、元角分、整。</para>
/// <para>全部匹配与转换逻辑均基于源生成预编译正则（GlobalUsings 已启用 GeneratedRegexAttribute）。</para>
/// <para>金额上限：绝对值 &lt; 1e26（秭级，decimal 有效位数约束）；小数四舍五入到分。</para>
/// </remarks>
public static partial class ChineseAmountConverter
{
    /// <summary>
    /// 大写字形映射串：索引 = 字符 - '-'，覆盖格式串中全部输出字符
    /// （'-'→负、'.'→元、'0'-'9'→零~玖、'A'→分、'B'→角、'C'-'E'→拾佰仟、'F'-'L'→万亿兆京垓秭穰）
    /// </summary>
    private const string DigitGlyphs = "负元空零壹贰叁肆伍陆柒捌玖空空空空空空空分角拾佰仟万亿兆京垓秭穰";

    /// <summary>大写数字字符（用于反向解析取值，索引即数值）</summary>
    private const string UpperDigits = "零壹贰叁肆伍陆柒捌玖";

    /// <summary>
    /// 专用数字格式串：# 为数字位，字母为字面量占位（A分 B角 C拾 D佰 E仟 F万 G亿 H兆 I京 J垓 K秭 L穰）
    /// </summary>
    private const string FormatMask = "#L#E#D#C#K#E#D#C#J#E#D#C#I#E#D#C#H#E#D#C#G#E#D#C#F#E#D#C#.0B0A";

    /// <summary>可转换金额上限（不含）：1e26，超出 decimal 28~29 位有效数字的安全表示范围</summary>
    private const decimal MaxExclusive = 100000000000000000000000000m;

    /// <summary>
    /// 阿拉伯数字金额转中文大写金额
    /// </summary>
    /// <param name="amount">金额（四舍五入到分）</param>
    /// <returns>中文大写金额，如 1234.56 → 壹仟贰佰叁拾肆元伍角陆分；0 → 零元整；负数带「负」前缀</returns>
    /// <exception cref="ArgumentOutOfRangeException">金额绝对值 ≥ 1e26 时抛出</exception>
    public static string ToChinese(decimal amount)
    {
        decimal rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (rounded == 0m)
        {
            return "零元整";
        }

        if (decimal.Abs(rounded) >= MaxExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "金额绝对值必须小于 1e26（秭级）");
        }

        // 负号单独提取：压缩正则会吞噬行首非数字字符（含负号），必须先取绝对值转换、末尾补「负」
        bool negative = rounded < 0m;

        // 1) 格式串展开为「数字 + 占位字母」序列，2) 正则压缩多余零位与单位，3) 正则逐字符映射大写字形
        string formatted = decimal.Abs(rounded).ToString(FormatMask, CultureInfo.InvariantCulture);
        string compacted = CompactionRegex().Replace(formatted, "${b}${z}");
        string chinese = GlyphRegex().Replace(compacted, static m => DigitGlyphs[m.Value[0] - '-'].ToString());

        // 零位收尾清理（全部经正则）
        chinese = ZeroBeforeYuanRegex().Replace(chinese, "元");
        chinese = ZeroJiaoZeroFenEndRegex().Replace(chinese, string.Empty);
        chinese = ZeroFenEndRegex().Replace(chinese, string.Empty);
        chinese = ZeroJiaoBeforeFenRegex().Replace(chinese, "零");
        chinese = ZeroJiaoEndRegex().Replace(chinese, string.Empty);
        chinese = LeadingZeroBeforeFenRegex().Replace(chinese, string.Empty);
        chinese = TrailingZerosRegex().Replace(chinese, string.Empty);

        if (!chinese.Contains('角') && !chinese.Contains('分'))
        {
            chinese += "整";
        }

        return negative ? "负" + chinese : chinese;
    }

    /// <summary>
    /// 中文大写金额转阿拉伯数字金额
    /// </summary>
    /// <param name="chinese">中文大写金额，如 壹仟贰佰叁拾肆元伍角陆分；支持「负」前缀、无「元」的纯角分写法（伍角）、省略「整」</param>
    /// <returns>对应金额</returns>
    /// <exception cref="ArgumentException">输入为 null / 空白时抛出</exception>
    /// <exception cref="FormatException">格式非法或超出 decimal 可表示范围时抛出</exception>
    public static decimal ToNumber(string? chinese)
    {
        if (string.IsNullOrWhiteSpace(chinese))
        {
            throw new ArgumentException("中文大写金额不能为空", nameof(chinese));
        }

        string input = chinese.Trim();
        Match master = MasterParseRegex().Match(input);
        if (!master.Success)
        {
            throw new FormatException($"无效的中文大写金额：{input}");
        }

        string intPart = master.Groups["int"].Success ? master.Groups["int"].Value : string.Empty;
        string fracPart = master.Groups["frac"].Success ? master.Groups["frac"].Value : string.Empty;
        bool hasFracUnit = fracPart.Contains('角') || fracPart.Contains('分');
        if (intPart.Length == 0 && !hasFracUnit)
        {
            throw new FormatException($"中文大写金额缺少「元」或「角 / 分」单位：{input}");
        }

        try
        {
            decimal result = 0m;

            if (intPart.Length > 0)
            {
                // 段首「拾」隐含壹（拾万 = 壹拾万），先经正则补全再切段
                intPart = LeadingTenRegex().Replace(intPart, "壹拾");
                int consumed = 0;
                foreach (Match section in SectionGroupRegex().Matches(intPart))
                {
                    result += ParseSection(section.Groups[1].Value) * Pow10(GroupPower(section.Groups[2].Value[0]));
                    consumed += section.Length;
                }

                result += ParseSection(intPart[consumed..]);
            }

            if (fracPart.Length > 0)
            {
                // 剥离合法角分片段后必须只剩零，否则为非法小数写法
                string rest = FracStripRegex().Replace(fracPart, string.Empty);
                if (!ZeroOnlyRegex().IsMatch(rest))
                {
                    throw new FormatException($"无效的中文大写金额小数段：{input}");
                }

                Match jiao = FracJiaoRegex().Match(fracPart);
                if (jiao.Success)
                {
                    result += DigitValue(jiao.Groups["v"].Value[0]) / 10m;
                }

                Match fen = FracFenRegex().Match(fracPart);
                if (fen.Success)
                {
                    result += DigitValue(fen.Groups["v"].Value[0]) / 100m;
                }
            }

            return master.Groups["neg"].Success ? -result : result;
        }
        catch (OverflowException ex)
        {
            throw new FormatException($"中文大写金额超出 decimal 可表示范围：{input}", ex);
        }
    }

    /// <summary>解析单个数字段（仟佰拾个，不含万亿级组单位），如 零贰佰 → 200</summary>
    private static long ParseSection(string section)
    {
        long value = 0;
        foreach (Match token in SectionTokenRegex().Matches(section))
        {
            int digit = DigitValue(token.Groups[1].Value[0]);
            int unit = token.Groups[2].Success ? UnitValue(token.Groups[2].Value[0]) : 1;
            value += digit * unit;
        }

        return value;
    }

    /// <summary>大写数字字符取值（零=0 … 玖=9）</summary>
    private static int DigitValue(char digit) => UpperDigits.IndexOf(digit);

    /// <summary>段内单位取值（拾=10、佰=100、仟=1000）</summary>
    private static int UnitValue(char unit) => unit switch
    {
        '拾' => 10,
        '佰' => 100,
        '仟' => 1000,
        _ => 1,
    };

    /// <summary>组单位对应的 10 的幂（万=4、亿=8、兆=12、京=16、垓=20、秭=24、穰=28）</summary>
    private static int GroupPower(char group) => group switch
    {
        '万' => 4,
        '亿' => 8,
        '兆' => 12,
        '京' => 16,
        '垓' => 20,
        '秭' => 24,
        '穰' => 28,
        _ => 0,
    };

    /// <summary>10 的幂（decimal）</summary>
    private static decimal Pow10(int power)
    {
        decimal value = 1m;
        for (int i = 0; i < power; i++)
        {
            value *= 10m;
        }

        return value;
    }

    /// <summary>零位压缩：去除格式串展开后多余的零与单位</summary>
    [GeneratedRegex(@"((?<=-|^)[^1-9]*)|((?'z'0)[0A-E]*((?=[1-9])|(?'-z'(?=[F-L\.]|$))))|((?'b'[F-L])(?'z'0)[0A-L]*((?=[1-9])|(?'-z'(?=[\.]|$))))", RegexOptions.Compiled | RegexOptions.CultureInvariant, 200)]
    private static partial Regex CompactionRegex();

    /// <summary>逐字符映射大写字形</summary>
    [GeneratedRegex(@".", RegexOptions.Compiled | RegexOptions.CultureInvariant, 200)]
    private static partial Regex GlyphRegex();

    /// <summary>元前的多余零（壹佰零元 → 壹佰元）</summary>
    [GeneratedRegex(@"零+元", RegexOptions.Compiled, 200)]
    private static partial Regex ZeroBeforeYuanRegex();

    /// <summary>末尾零角零分（壹佰元零角零分 → 壹佰元）</summary>
    [GeneratedRegex(@"零角零分$", RegexOptions.Compiled, 200)]
    private static partial Regex ZeroJiaoZeroFenEndRegex();

    /// <summary>末尾零分（壹元伍角零分 → 壹元伍角）</summary>
    [GeneratedRegex(@"零分$", RegexOptions.Compiled, 200)]
    private static partial Regex ZeroFenEndRegex();

    /// <summary>分位非零时的零角（壹元零角伍分 → 壹元零伍分）</summary>
    [GeneratedRegex(@"零角(?=[零壹贰叁肆伍陆柒捌玖]分)", RegexOptions.Compiled, 200)]
    private static partial Regex ZeroJiaoBeforeFenRegex();

    /// <summary>末尾零角（壹元零角 → 壹元）</summary>
    [GeneratedRegex(@"零角$", RegexOptions.Compiled, 200)]
    private static partial Regex ZeroJiaoEndRegex();

    /// <summary>纯分金额的前导零（零伍分 → 伍分）</summary>
    [GeneratedRegex(@"^零(?=[壹贰叁肆伍陆柒捌玖]分)", RegexOptions.Compiled, 200)]
    private static partial Regex LeadingZeroBeforeFenRegex();

    /// <summary>末尾多余零（伍角零 → 伍角）</summary>
    [GeneratedRegex(@"零+$", RegexOptions.Compiled, 200)]
    private static partial Regex TrailingZerosRegex();

    /// <summary>大写金额主校验：负号 / 整数段 + 元 / 小数段 / 整</summary>
    [GeneratedRegex(@"^(?<neg>负)?(?:(?<int>[零壹贰叁肆伍陆柒捌玖拾佰仟万亿兆京垓秭穰]+?)元)?(?<frac>[零壹贰叁肆伍陆柒捌玖角分]*?)整?$", RegexOptions.Compiled, 200)]
    private static partial Regex MasterParseRegex();

    /// <summary>整数段按组单位（穰秭垓京兆亿万）切段</summary>
    [GeneratedRegex(@"([零壹贰叁肆伍陆柒捌玖拾佰仟]*)(穰|秭|垓|京|兆|亿|万)", RegexOptions.Compiled, 200)]
    private static partial Regex SectionGroupRegex();

    /// <summary>段内数字 + 可选单位（拾佰仟）逐位解析</summary>
    [GeneratedRegex(@"([零壹贰叁肆伍陆柒捌玖])([拾佰仟])?", RegexOptions.Compiled, 200)]
    private static partial Regex SectionTokenRegex();

    /// <summary>段首隐含壹的拾（拾万 → 壹拾万）</summary>
    [GeneratedRegex(@"(?<![零壹贰叁肆伍陆柒捌玖])拾", RegexOptions.Compiled, 200)]
    private static partial Regex LeadingTenRegex();

    /// <summary>小数段剥离合法角分片段（用于残余校验）</summary>
    [GeneratedRegex(@"[零壹贰叁肆伍陆柒捌玖]角|[零壹贰叁肆伍陆柒捌玖]分", RegexOptions.Compiled, 200)]
    private static partial Regex FracStripRegex();

    /// <summary>角位取值</summary>
    [GeneratedRegex(@"(?<v>[零壹贰叁肆伍陆柒捌玖])角", RegexOptions.Compiled, 200)]
    private static partial Regex FracJiaoRegex();

    /// <summary>分位取值</summary>
    [GeneratedRegex(@"(?<v>[零壹贰叁肆伍陆柒捌玖])分", RegexOptions.Compiled, 200)]
    private static partial Regex FracFenRegex();

    /// <summary>残余串全零校验</summary>
    [GeneratedRegex(@"^零*$", RegexOptions.Compiled, 200)]
    private static partial Regex ZeroOnlyRegex();
}
