using Ino.Domains.Travel.Rfw;

namespace Ino.Domains.Travel.TripPlanner;

/// <summary>
/// Hardcoded climatology + forecast per destination/month. Stays static
/// (no DI, no interface) to match the FlightSearch.MockFlightCorpus
/// pattern — when the LlmNeuron rewrite lands, this whole class swaps
/// for an <c>IWeatherTool</c> backed by Open-Meteo, and TripPlanner
/// rewires to the interface. The shapes the builders consume don't change.
/// </summary>
internal static class MockWeatherCorpus
{
    public static WeatherClimatology GetClimatology(string destination, string month)
    {
        // Tightly scoped to v0.1 demo destinations. Falls back to a generic
        // "shoulder season" for anything else so the plan never breaks if
        // someone types "plan a trip to ulaanbaatar".
        return (destination?.ToLowerInvariant(), Normalize(month)) switch
        {
            ("bali",  "june") => new(destination!, "June",     "dry",      28, 0.15),
            ("bali",  "july") => new(destination!, "July",     "dry",      27, 0.10),
            ("bali",  "december") => new(destination!, "December", "wet",   29, 0.65),
            ("tokyo", "april") => new(destination!, "April",   "shoulder", 17, 0.30),
            ("tokyo", "june") => new(destination!, "June",     "wet",      24, 0.55),
            ("paris", "june") => new(destination!, "June",     "dry",      22, 0.25),
            _ => new(destination ?? "your destination", PrettyMonth(month), "shoulder", 22, 0.30),
        };
    }

    public static WeatherForecast GetForecast(string location, DateOnly date)
    {
        // Deterministic stand-in: even days are sunny, odd days are
        // partly cloudy, every fifth day is rainy. Keeps the
        // weather-aware activity rule firing on a known schedule so the
        // BDD test asserts predictably.
        if (date.Day % 5 == 0)
            return new(location, date, "rain",   24, 0.80);
        if (date.Day % 2 == 0)
            return new(location, date, "sunny",  29, 0.10);
        return new(location, date, "cloudy", 26, 0.30);
    }

    static string Normalize(string month) => month?.Trim().ToLowerInvariant() ?? string.Empty;

    static string PrettyMonth(string month)
    {
        if (string.IsNullOrWhiteSpace(month)) return "this season";
        return char.ToUpperInvariant(month[0]) + month.Substring(1).ToLowerInvariant();
    }
}
