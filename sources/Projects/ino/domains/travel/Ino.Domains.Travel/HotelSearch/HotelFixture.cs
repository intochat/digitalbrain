using Ino.Domains.Travel.Contracts;

namespace Ino.Domains.Travel.HotelSearch;

/// <summary>
/// Seed hotels keyed by destination. Same shape as
/// FlightSearch.FlightFixture — the v0.1 demo runs entirely off these
/// in-process catalogues.
/// </summary>
public static class HotelFixture
{
    public static IReadOnlyDictionary<string, HotelSummary[]> ByDestination { get; } =
        new Dictionary<string, HotelSummary[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tokyo"] =
            [
                new HotelSummary("Park Hyatt Tokyo", "Shinjuku", 620, 4.7, 5, 4),
                new HotelSummary("Aman Tokyo", "Otemachi", 1450, 4.9, 5, 3),
                new HotelSummary("Hotel Niwa Tokyo", "Kanda", 280, 4.5, 4, 4),
            ],
            ["Paris"] =
            [
                new HotelSummary("Le Bristol Paris", "8th arr.", 1120, 4.8, 5, 3),
                new HotelSummary("Hôtel Costes", "1st arr.", 780, 4.6, 5, 4),
                new HotelSummary("Hôtel des Grands Boulevards", "2nd arr.", 320, 4.5, 4, 4),
            ],
            ["NYC"] =
            [
                new HotelSummary("The Greenwich Hotel", "Tribeca", 920, 4.8, 5, 3),
                new HotelSummary("The Beekman", "Financial District", 510, 4.6, 5, 4),
                new HotelSummary("The Standard High Line", "Meatpacking", 480, 4.4, 4, 4),
            ],
        };

    public static HotelSummary[] For(string? destination) =>
        !string.IsNullOrWhiteSpace(destination) && ByDestination.TryGetValue(destination, out var hotels)
            ? hotels
            : ByDestination["Tokyo"];
}
