using Ino.Domains.Travel.Contracts;

namespace Ino.Domains.Travel.FlightSearch;

/// <summary>
/// Hardcoded seed flights keyed by destination. Replaces the v0.1 single-Bali
/// constant with a small catalogue covering the three demo destinations
/// (Tokyo, Paris, NYC). A later slice swaps this for a real flight provider
/// (TripRadar / serpapi) — keeping the lookup shape (<c>For(destination)</c>)
/// the same so the swap is mechanical.
///
/// Returned as <see cref="FlightSummary"/>[] (not IReadOnlyList) so the
/// runtime type is a concrete T[] that Orleans' serializer supports —
/// collection-expression bindings to IReadOnlyList&lt;T&gt; materialize as
/// the synthesized <c>&lt;&gt;z__ReadOnlyArray&lt;T&gt;</c>, which has no
/// generated copier and trips CodecNotFoundException when
/// <see cref="FlightCardResponse"/> travels cross-silo.
/// </summary>
public static class FlightFixture
{
    public static IReadOnlyDictionary<string, FlightSummary[]> ByDestination { get; } =
        new Dictionary<string, FlightSummary[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tokyo"] =
            [
                new FlightSummary("ANA", "JFK", "HND", 1180, "13:30", "13h 50m"),
                new FlightSummary("Japan Airlines", "JFK", "NRT", 1095, "11:55", "14h 05m"),
                new FlightSummary("United", "EWR", "HND", 985, "16:45", "14h 25m"),
            ],
            ["Paris"] =
            [
                new FlightSummary("Air France", "JFK", "CDG", 720, "21:30", "7h 25m"),
                new FlightSummary("Delta", "JFK", "CDG", 685, "22:45", "7h 35m"),
                new FlightSummary("United", "EWR", "CDG", 740, "20:50", "7h 40m"),
            ],
            ["NYC"] =
            [
                new FlightSummary("United", "SFO", "JFK", 320, "06:00", "5h 30m"),
                new FlightSummary("JetBlue", "LAX", "JFK", 295, "08:15", "5h 45m"),
                new FlightSummary("Delta", "ATL", "LGA", 240, "09:00", "2h 30m"),
            ],
        };

    /// <summary>Lookup by destination; falls back to Tokyo when no key matches.</summary>
    public static FlightSummary[] For(string? destination) =>
        !string.IsNullOrWhiteSpace(destination) && ByDestination.TryGetValue(destination, out var flights)
            ? flights
            : ByDestination["Tokyo"];
}
