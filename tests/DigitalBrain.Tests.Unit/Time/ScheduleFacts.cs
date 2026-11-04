using DigitalBrain.Time;

namespace DigitalBrain.Tests.Unit.Time;

public sealed class ScheduleFacts
{
    [Theory]
    [InlineData("07:00", ScheduleKind.Daily, 24 * 60)]
    [InlineData("Mon 00:00", ScheduleKind.Weekly, 7 * 24 * 60)]
    [InlineData("every 5m", ScheduleKind.Interval, 5)]
    [InlineData("every 2h", ScheduleKind.Interval, 120)]
    public void ParsesTheGrammar(string text, ScheduleKind kind, int minutes)
    {
        var s = Schedule.Parse(text);
        Assert.Equal(kind, s.Kind);
        Assert.Equal(TimeSpan.FromMinutes(minutes), s.Period);
    }

    [Theory]
    [InlineData("7:00pm")]
    [InlineData("every 30s")]
    [InlineData("every 0m")]
    [InlineData("Monday 00:00")]
    [InlineData("mon 00:00")]
    [InlineData("")]
    [InlineData("07:00\n")]
    [InlineData("Mon 09:00\n")]
    [InlineData("every 5m\n")]
    public void RefusesAnythingElse(string text) => Assert.Throws<FormatException>(() => Schedule.Parse(text));

    [Fact]
    public void DailyNextAfterIsTheNextOccurrenceInUtc()
    {
        var s = Schedule.Parse("07:00");
        var now = new DateTimeOffset(2026, 11, 4, 8, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 7, 0, 0, TimeSpan.Zero), s.NextAfter(now));
    }

    [Fact]
    public void DailyNextAfterIsStrictlyAfterNow()
    {
        var s = Schedule.Parse("07:00");
        var now = new DateTimeOffset(2026, 11, 4, 7, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 7, 0, 0, TimeSpan.Zero), s.NextAfter(now));
    }

    [Fact]
    public void WeeklyNextAfterLandsOnTheDay()
    {
        var s = Schedule.Parse("Mon 00:00");
        var now = new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero); // a Wednesday
        Assert.Equal(DayOfWeek.Monday, s.NextAfter(now).DayOfWeek);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero), s.NextAfter(now));
    }

    [Fact]
    public void WeeklyNextAfterExactlyOnTheOccurrenceYieldsTheNextWeek()
    {
        var now = new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero); // a Monday
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero), Schedule.Parse("Mon 00:00").NextAfter(now));
    }

    [Fact]
    public void WeeklyNextAfterWrapsFromSunday()
    {
        var now = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero); // a Sunday
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero), Schedule.Parse("Mon 00:00").NextAfter(now));
    }

    [Fact]
    public void IntervalNextAfterAddsThePeriod()
    {
        var now = new DateTimeOffset(2026, 11, 4, 8, 0, 0, TimeSpan.Zero);
        Assert.Equal(now.AddMinutes(5), Schedule.Parse("every 5m").NextAfter(now));
    }
}
