using System.Globalization;
using System.Text.RegularExpressions;

namespace DigitalBrain.Time;

/// <summary>How often a schedule fires.</summary>
public enum ScheduleKind
{
    /// <summary>Every day at a UTC time.</summary>
    Daily,

    /// <summary>Every week on a day at a UTC time.</summary>
    Weekly,

    /// <summary>Every fixed number of minutes or hours.</summary>
    Interval,
}

/// <summary>A schedule: <c>HH:mm</c>, <c>Mon HH:mm</c>, <c>every Nm</c> or <c>every Nh</c>; all times are UTC.</summary>
/// <param name="Text">The text it was parsed from.</param>
/// <param name="Kind">Daily, weekly or interval.</param>
/// <param name="Period">One day, seven days or the interval.</param>
/// <param name="Day">The weekday, for weekly schedules; otherwise null.</param>
/// <param name="At">The time of day, for daily and weekly schedules; otherwise null.</param>
public sealed partial record Schedule(string Text, ScheduleKind Kind, TimeSpan Period, DayOfWeek? Day, TimeOnly? At)
{
    private static readonly string[] Days = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    /// <summary>Parses one of the four forms.</summary>
    /// <exception cref="FormatException">The text is not a schedule.</exception>
    public static Schedule Parse(string text)
    {
        if (text is not null)
        {
            var daily = DailyRule().Match(text);
            if (daily.Success)
            {
                return new Schedule(text, ScheduleKind.Daily, TimeSpan.FromDays(1), null, Time(daily.Groups["t"].Value));
            }

            var weekly = WeeklyRule().Match(text);
            if (weekly.Success)
            {
                var day = (DayOfWeek)Array.IndexOf(Days, weekly.Groups["d"].Value);
                return new Schedule(text, ScheduleKind.Weekly, TimeSpan.FromDays(7), day, Time(weekly.Groups["t"].Value));
            }

            var every = IntervalRule().Match(text);
            if (every.Success && int.TryParse(every.Groups["n"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1)
            {
                var period = every.Groups["u"].Value == "h" ? TimeSpan.FromHours(n) : TimeSpan.FromMinutes(n);
                return new Schedule(text, ScheduleKind.Interval, period, null, null);
            }
        }

        throw new FormatException($"'{text}' is not a schedule (use HH:mm, Mon HH:mm, every 5m or every 2h).");
    }

    /// <summary>The next occurrence strictly after <paramref name="now"/>, in UTC; for an interval, <paramref name="now"/> plus the period.</summary>
    public DateTimeOffset NextAfter(DateTimeOffset now)
    {
        if (Kind == ScheduleKind.Interval)
        {
            return now + Period;
        }

        var utc = now.UtcDateTime;
        var at = At ?? TimeOnly.MinValue;
        var candidate = new DateTimeOffset(DateOnly.FromDateTime(utc).ToDateTime(at), TimeSpan.Zero);
        if (Kind == ScheduleKind.Weekly)
        {
            var ahead = ((int)(Day ?? DayOfWeek.Sunday) - (int)candidate.DayOfWeek + 7) % 7;
            candidate = candidate.AddDays(ahead);
        }

        return candidate > now ? candidate : candidate + Period;
    }

    private static TimeOnly Time(string hhmm) => TimeOnly.ParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture);

    [GeneratedRegex("^(?<t>([01][0-9]|2[0-3]):[0-5][0-9])\\z", RegexOptions.CultureInvariant)]
    private static partial Regex DailyRule();

    [GeneratedRegex("^(?<d>Mon|Tue|Wed|Thu|Fri|Sat|Sun) (?<t>([01][0-9]|2[0-3]):[0-5][0-9])\\z", RegexOptions.CultureInvariant)]
    private static partial Regex WeeklyRule();

    [GeneratedRegex("^every (?<n>[0-9]{1,6})(?<u>[mh])\\z", RegexOptions.CultureInvariant)]
    private static partial Regex IntervalRule();
}
