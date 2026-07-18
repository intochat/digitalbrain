using System.Globalization;
using System.Text.RegularExpressions;

namespace Ino.Domains.Travel.TripPlanner;

/// <summary>
/// Cheap regex-based extraction of destination + month from a free-form
/// "plan a trip to bali next month" prompt. Tuned for the rich plan's
/// two required slots; falls back to "your destination" + "this season"
/// so the plan never crashes on an unparseable prompt.
/// </summary>
internal static partial class PlanTripPromptParser
{
    [GeneratedRegex(@"\bto\s+([a-zA-Z][a-zA-Z\s]+?)(?:\s+(?:next|this|in|on|for|tomorrow|today|\d)|$)",
        RegexOptions.IgnoreCase)]
    private static partial Regex DestinationRx();

    [GeneratedRegex(@"\b(january|february|march|april|may|june|july|august|september|october|november|december)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex MonthRx();

    [GeneratedRegex(@"\bnext\s+month\b", RegexOptions.IgnoreCase)]
    private static partial Regex NextMonthRx();

    public static string ExtractDestination(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return "your destination";
        var m = DestinationRx().Match(prompt);
        if (!m.Success) return "your destination";
        var raw = m.Groups[1].Value.Trim();
        // Title-case the first word so "bali" → "Bali" reads right in the
        // confirmation card.
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(raw.ToLowerInvariant());
    }

    public static string ExtractMonth(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return "this season";

        var named = MonthRx().Match(prompt);
        if (named.Success)
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(named.Value.ToLowerInvariant());

        if (NextMonthRx().IsMatch(prompt))
        {
            var nextMonth = DateTime.UtcNow.AddMonths(1);
            return nextMonth.ToString("MMMM", CultureInfo.InvariantCulture);
        }

        return "this season";
    }
}
