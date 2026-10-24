/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 21:30:00 +08:00 中国标准时间 星期四
 * 描述：六段式 CRON 表达式解析器（秒 分 时 日 月 周 [年]），支持 * ? - / , 与未来 N 次预览
 */

namespace Qlx.BuildingBlocks.Jobs;

/// <summary>
/// CRON 表达式解析异常。携带字段名与出错位置，便于运维定位非法表达式。
/// </summary>
public sealed class CronParseException : Exception
{
    /// <summary>出错的 CRON 字段名（秒/分/时/日/月/周/年）。</summary>
    public string FieldName { get; }

    /// <summary>字段在表达式中的序号（0 起）。</summary>
    public int FieldIndex { get; }

    /// <summary>
    /// 构造解析异常。
    /// </summary>
    /// <param name="fieldName">出错的字段名。</param>
    /// <param name="fieldIndex">字段序号（0 起）。</param>
    /// <param name="message">错误详情。</param>
    public CronParseException(string fieldName, int fieldIndex, string message)
        : base($"CRON 表达式第 {fieldIndex + 1} 段（{fieldName}）解析失败：{message}")
    {
        FieldName = fieldName;
        FieldIndex = fieldIndex;
    }
}

/// <summary>
/// 六段式 CRON 调度计划：<c>秒 分 时 日 月 周 [年]</c>。
/// <para>
/// 支持通配符 <c>*</c>、无具体值占位 <c>?</c>（仅日/周段）、区间 <c>a-b</c>、步长 <c>/n</c>、
/// 列表 <c>a,b,c</c>；周段 0 与 7 均表示周日。日（DOM）与周（DOW）同时受限时按标准 CRON
/// 语义取「或」匹配，否则仅受限制的一方生效。
/// </para>
/// </summary>
public sealed class SixFieldCronSchedule
{
    private const int MaxYearsHorizon = 5;

    private static readonly string[] s_fieldNames = ["秒", "分", "时", "日", "月", "周", "年"];

    private readonly CronField _second;
    private readonly CronField _minute;
    private readonly CronField _hour;
    private readonly CronField _day;
    private readonly CronField _month;
    private readonly CronField _dayOfWeek;
    private readonly CronField? _year;
    private readonly bool _dayRestricted;
    private readonly bool _dayOfWeekRestricted;

    /// <summary>
    /// 解析六段式 CRON 表达式。
    /// </summary>
    /// <param name="expression">形如 <c>0 0 3 * * *</c> 或带年段 <c>0 0 3 1 1 * 2027</c> 的表达式。</param>
    /// <exception cref="ArgumentException">表达式为空白。</exception>
    /// <exception cref="CronParseException">任一字段非法时抛出，异常消息含字段定位。</exception>
    public SixFieldCronSchedule(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        var parts = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 6 or > 7)
        {
            throw new CronParseException("整体", 0,
                $"表达式必须为 6 段（秒 分 时 日 月 周）或 7 段（追加年），实际 {parts.Length} 段：{expression}");
        }

        _second = ParseField(parts[0], 0, 0, 59, allowQuestionMark: false);
        _minute = ParseField(parts[1], 1, 0, 59, allowQuestionMark: false);
        _hour = ParseField(parts[2], 2, 0, 23, allowQuestionMark: false);
        _day = ParseField(parts[3], 3, 1, 31, allowQuestionMark: true);
        _month = ParseField(parts[4], 4, 1, 12, allowQuestionMark: false);
        _dayOfWeek = ParseField(parts[5], 5, 0, 7, allowQuestionMark: true);
        _year = parts.Length == 7 ? ParseField(parts[6], 6, 1970, 2099, allowQuestionMark: true) : null;

        // 标准 CRON 语义：日/周均显式受限（非 * 非 ?）时取「或」；否则未限制一方恒匹配
        _dayRestricted = !_day.IsWildcard;
        _dayOfWeekRestricted = !_dayOfWeek.IsWildcard;
    }

    /// <summary>
    /// 计算晚于 <paramref name="after"/> 的下一次触发时刻（UTC，截断到秒）。
    /// </summary>
    /// <param name="after">基准时刻。</param>
    /// <returns>下一次触发时刻。</returns>
    /// <exception cref="InvalidOperationException">未来 5 年内无匹配时刻（年段约束过窄等）。</exception>
    public DateTimeOffset NextOccurrence(DateTimeOffset after)
    {
        var candidate = TruncateToSecond(after.ToUniversalTime()).AddSeconds(1);
        var horizon = candidate.AddYears(MaxYearsHorizon);

        while (candidate <= horizon)
        {
            if (_year is { IsWildcard: false } && !_year.Contains(candidate.Year))
            {
                candidate = new DateTimeOffset(candidate.Year + 1, 1, 1, 0, 0, 0, TimeSpan.Zero);
                continue;
            }

            if (!_month.Contains(candidate.Month))
            {
                candidate = new DateTimeOffset(candidate.Year, candidate.Month, 1, 0, 0, 0, TimeSpan.Zero)
                    .AddMonths(1);
                continue;
            }

            if (!MatchesDay(candidate))
            {
                candidate = new DateTimeOffset(candidate.Year, candidate.Month, candidate.Day, 0, 0, 0, TimeSpan.Zero)
                    .AddDays(1);
                continue;
            }

            if (!_hour.Contains(candidate.Hour))
            {
                candidate = TruncateToHour(candidate).AddHours(1);
                continue;
            }

            if (!_minute.Contains(candidate.Minute))
            {
                candidate = TruncateToMinute(candidate).AddMinutes(1);
                continue;
            }

            if (_second.Contains(candidate.Second))
            {
                return candidate;
            }

            candidate = candidate.AddSeconds(1);
        }

        throw new InvalidOperationException(
            $"CRON 表达式在未来 {MaxYearsHorizon} 年内无匹配时刻（年段约束或字段组合过窄）。");
    }

    /// <summary>
    /// 预览未来 N 次触发时刻（UTC）。
    /// </summary>
    /// <param name="count">预览次数（1~1000）。</param>
    /// <param name="after">基准时刻。</param>
    /// <returns>按时间升序的触发时刻列表。</returns>
    public IReadOnlyList<DateTimeOffset> NextOccurrences(int count, DateTimeOffset after)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 1000);

        var results = new List<DateTimeOffset>(count);
        var cursor = after;
        for (var i = 0; i < count; i++)
        {
            cursor = NextOccurrence(cursor);
            results.Add(cursor);
        }

        return results;
    }

    /// <summary>判定给定时刻的「日」是否匹配（含日/周受限组合语义）。</summary>
    private bool MatchesDay(DateTimeOffset candidate)
    {
        var dayMatches = _day.Contains(candidate.Day);
        // .NET DayOfWeek: Sunday=0，与 CRON 0=周日一致；7 亦表示周日
        var cronDow = (int)candidate.DayOfWeek;
        var dowMatches = _dayOfWeek.Contains(cronDow) || (_dayOfWeek.Contains(7) && cronDow == 0);

        return (_dayRestricted, _dayOfWeekRestricted) switch
        {
            (true, true) => dayMatches || dowMatches,
            (true, false) => dayMatches,
            (false, true) => dowMatches,
            _ => true,
        };
    }

    private static DateTimeOffset TruncateToSecond(DateTimeOffset value)
        => new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, value.Offset);

    private static DateTimeOffset TruncateToMinute(DateTimeOffset value)
        => new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Offset);

    private static DateTimeOffset TruncateToHour(DateTimeOffset value)
        => new(value.Year, value.Month, value.Day, value.Hour, 0, 0, value.Offset);

    /// <summary>解析单个 CRON 字段（支持 * ? , - /）。</summary>
    private static CronField ParseField(string text, int index, int min, int max, bool allowQuestionMark)
    {
        var name = s_fieldNames[index];
        if (allowQuestionMark && text == "?")
        {
            return CronField.Wildcard;
        }

        if (text == "*")
        {
            return CronField.Wildcard;
        }

        var values = new HashSet<int>();
        foreach (var token in text.Split(','))
        {
            ParseToken(token.Trim(), name, index, min, max, values);
        }

        if (values.Count == 0)
        {
            throw new CronParseException(name, index, "字段未产生任何有效取值");
        }

        return new CronField(values, isWildcard: false);
    }

    /// <summary>解析单个逗号分段：字面量 / 区间 / 步长组合。</summary>
    private static void ParseToken(string token, string name, int index, int min, int max, HashSet<int> values)
    {
        var rangePart = token;
        int step = 1;
        var slashIndex = token.IndexOf('/');
        if (slashIndex >= 0)
        {
            rangePart = token[..slashIndex];
            var stepText = token[(slashIndex + 1)..];
            if (!int.TryParse(stepText, out step) || step <= 0)
            {
                throw new CronParseException(name, index, $"步长「{stepText}」必须为正整数");
            }
        }

        int rangeStart;
        int rangeEnd;
        if (rangePart == "*" || (rangePart == "?" && name is "日" or "周"))
        {
            rangeStart = min;
            rangeEnd = max;
        }
        else
        {
            var dashIndex = rangePart.IndexOf('-');
            if (dashIndex >= 0)
            {
                rangeStart = ParseValue(rangePart[..dashIndex], name, index, min, max);
                rangeEnd = ParseValue(rangePart[(dashIndex + 1)..], name, index, min, max);
                if (rangeStart > rangeEnd)
                {
                    throw new CronParseException(name, index, $"区间下界 {rangeStart} 大于上界 {rangeEnd}");
                }
            }
            else
            {
                rangeStart = ParseValue(rangePart, name, index, min, max);
                // 「a/n」语义：从 a 起以步长 n 到字段上限（Quartz 惯例）
                rangeEnd = slashIndex >= 0 ? max : rangeStart;
            }
        }

        for (var value = rangeStart; value <= rangeEnd; value += step)
        {
            _ = values.Add(value);
        }
    }

    private static int ParseValue(string text, string name, int index, int min, int max)
    {
        if (!int.TryParse(text, out var value) || value < min || value > max)
        {
            throw new CronParseException(name, index, $"取值「{text}」超出范围 [{min}, {max}] 或非法");
        }

        return value;
    }

    /// <summary>CRON 字段的取值集合（通配符时为 null，恒匹配）。</summary>
    private sealed class CronField(HashSet<int>? values, bool isWildcard)
    {
        /// <summary>全字段通配（* 或 ?）实例。</summary>
        public static readonly CronField Wildcard = new(null, true);

        private readonly HashSet<int>? _values = values;

        /// <summary>是否通配（任意值均匹配）。</summary>
        public bool IsWildcard { get; } = isWildcard;

        /// <summary>判定具体值是否命中该字段。</summary>
        public bool Contains(int value) => IsWildcard || (_values?.Contains(value) ?? false);
    }
}
