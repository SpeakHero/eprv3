/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 04:40:00 +08:00 中国标准时间 星期五
 * 描述：六段式 CRON 解析器测试——段数 / 范围 / 通配 / 区间 / 步长 / 日周组合 / 年段约束与定位异常
 */

using Qlx.BuildingBlocks.Jobs;

namespace Qlx.BuildingBlocks.Tests.Jobs;

/// <summary>
/// <see cref="SixFieldCronSchedule"/> 单元测试。全部断言基于显式 UTC 基准时刻，不依赖墙钟。
/// </summary>
public sealed class SixFieldCronScheduleTests
{
    /// <summary>基准时刻：2026-10-21（周三）10:00:00 UTC。</summary>
    private static readonly DateTimeOffset s_base = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ParseSixFieldsValid()
    {
        _ = new SixFieldCronSchedule("0 30 2 * * *");
    }

    [Fact]
    public void ParseSevenFieldsWithYearValid()
    {
        _ = new SixFieldCronSchedule("0 0 0 1 1 * 2027");
    }

    [Fact]
    public void ParseWrongFieldCountThrowsWithWholeFieldLocator()
    {
        var ex = Assert.Throws<CronParseException>(() => _ = new SixFieldCronSchedule("0 0 3 * *"));
        Assert.Equal("整体", ex.FieldName);
        Assert.Equal(0, ex.FieldIndex);
    }

    [Fact]
    public void ParseTooManyFieldsThrows()
    {
        var ex = Assert.Throws<CronParseException>(() => _ = new SixFieldCronSchedule("0 0 3 * * * 2027 9"));
        Assert.Equal("整体", ex.FieldName);
    }

    [Fact]
    public void ParseSecondOutOfRangeThrowsWithFieldLocator()
    {
        var ex = Assert.Throws<CronParseException>(() => _ = new SixFieldCronSchedule("60 0 0 * * *"));
        Assert.Equal("秒", ex.FieldName);
        Assert.Equal(0, ex.FieldIndex);
    }

    [Fact]
    public void ParseMinuteOutOfRangeThrows()
    {
        var ex = Assert.Throws<CronParseException>(() => _ = new SixFieldCronSchedule("0 60 0 * * *"));
        Assert.Equal("分", ex.FieldName);
        Assert.Equal(1, ex.FieldIndex);
    }

    [Fact]
    public void ParseHourOutOfRangeThrows()
    {
        var ex = Assert.Throws<CronParseException>(() => _ = new SixFieldCronSchedule("0 0 24 * * *"));
        Assert.Equal("时", ex.FieldName);
        Assert.Equal(2, ex.FieldIndex);
    }

    [Fact]
    public void ParseMonthOutOfRangeThrows()
    {
        var ex = Assert.Throws<CronParseException>(() => _ = new SixFieldCronSchedule("0 0 0 * 13 *"));
        Assert.Equal("月", ex.FieldName);
        Assert.Equal(4, ex.FieldIndex);
    }

    [Fact]
    public void ParseNonPositiveStepThrows()
    {
        var ex = Assert.Throws<CronParseException>(() => _ = new SixFieldCronSchedule("*/0 * * * * *"));
        Assert.Equal("秒", ex.FieldName);
    }

    [Fact]
    public void ParseReversedRangeThrows()
    {
        var ex = Assert.Throws<CronParseException>(() => _ = new SixFieldCronSchedule("0 0 0 10-5 * *"));
        Assert.Equal("日", ex.FieldName);
        Assert.Equal(3, ex.FieldIndex);
    }

    [Fact]
    public void ParseBlankExpressionThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _ = new SixFieldCronSchedule("   "));
    }

    [Fact]
    public void NextOccurrenceEverySecondAdvancesFromBasePlusOneSecond()
    {
        var schedule = new SixFieldCronSchedule("* * * * * *");

        var next = schedule.NextOccurrence(s_base);

        Assert.Equal(new DateTimeOffset(2026, 10, 21, 10, 0, 1, TimeSpan.Zero), next);
    }

    [Fact]
    public void NextOccurrenceStepSecondsFiresAtNextStepBoundary()
    {
        var schedule = new SixFieldCronSchedule("*/15 * * * * *");

        var next = schedule.NextOccurrence(s_base);

        Assert.Equal(new DateTimeOffset(2026, 10, 21, 10, 0, 15, TimeSpan.Zero), next);
    }

    [Fact]
    public void NextOccurrenceDailyAt0230SkipsToNextDay()
    {
        var schedule = new SixFieldCronSchedule("0 30 2 * * *");

        var next = schedule.NextOccurrence(s_base);

        Assert.Equal(new DateTimeOffset(2026, 10, 22, 2, 30, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void NextOccurrenceListMinutesPicksNearestValue()
    {
        var schedule = new SixFieldCronSchedule("0 0,30 8 * * *");

        var next = schedule.NextOccurrence(s_base);

        Assert.Equal(new DateTimeOffset(2026, 10, 22, 8, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void NextOccurrenceWeekRestrictionFiresOnSunday()
    {
        // 周三 10:00 起，每周日 0 点触发
        var schedule = new SixFieldCronSchedule("0 0 0 * * 0");

        var next = schedule.NextOccurrence(s_base);

        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero), next);
        Assert.Equal(DayOfWeek.Sunday, next.DayOfWeek);
    }

    [Fact]
    public void NextOccurrenceDowSevenAlsoMeansSunday()
    {
        var schedule = new SixFieldCronSchedule("0 0 0 * * 7");

        var next = schedule.NextOccurrence(s_base);

        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero), next);
        Assert.Equal(DayOfWeek.Sunday, next.DayOfWeek);
    }

    [Fact]
    public void NextOccurrenceDayAndWeekRestrictedTakesUnionSemantics()
    {
        // 日=1 且 周=1（周一）同时受限：标准 CRON 语义取「或」（每月 1 日或每周一均触发）
        var schedule = new SixFieldCronSchedule("0 0 0 1 * 1");

        var next = schedule.NextOccurrence(s_base);

        // 2026-10-21 周三之后最近的匹配日：10-26（周一）早于 11-01（每月 1 日）
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void NextOccurrenceQuestionMarkInDayAndWeekActsAsWildcard()
    {
        var schedule = new SixFieldCronSchedule("0 0 0 ? * ?");

        var next = schedule.NextOccurrence(s_base);

        Assert.Equal(new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void NextOccurrenceFutureYearConstraintFiresInThatYear()
    {
        var schedule = new SixFieldCronSchedule("0 0 0 1 1 * 2027");

        var next = schedule.NextOccurrence(s_base);

        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void NextOccurrenceYearBeyondHorizonThrows()
    {
        var schedule = new SixFieldCronSchedule("0 0 0 1 1 * 2070");

        Assert.Throws<InvalidOperationException>(() => schedule.NextOccurrence(s_base));
    }

    [Fact]
    public void NextOccurrencesReturnsMonotonicSequenceOfRequestedCount()
    {
        var schedule = new SixFieldCronSchedule("*/30 * * * * *");

        var occurrences = schedule.NextOccurrences(5, s_base);

        Assert.Equal(5, occurrences.Count);
        for (var i = 0; i < occurrences.Count; i++)
        {
            Assert.Equal(s_base.AddSeconds(30 * (i + 1)), occurrences[i]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void NextOccurrencesCountOutOfRangeThrows(int count)
    {
        var schedule = new SixFieldCronSchedule("* * * * * *");

        Assert.Throws<ArgumentOutOfRangeException>(() => schedule.NextOccurrences(count, s_base));
    }
}
