using Ino.Domains.Travel.Contracts;

namespace Ino.Domains.Travel.PlaceSearch;

/// <summary>
/// Seed places-of-interest keyed by destination — landmarks, restaurants, and
/// neighbourhoods chosen to make the day-by-day itinerary feel concrete in
/// the demo. Same lookup shape as FlightSearch.FlightFixture.
/// </summary>
public static class PlaceFixture
{
    public static IReadOnlyDictionary<string, PlaceSummary[]> ByDestination { get; } =
        new Dictionary<string, PlaceSummary[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tokyo"] =
            [
                new PlaceSummary("Senso-ji Temple", "Landmark", 4.6, 78_412, "Asakusa's seventh-century temple — go at dawn before the crowds."),
                new PlaceSummary("teamLab Planets", "Museum", 4.7, 32_104, "Immersive digital-art bath in Toyosu; book months ahead."),
                new PlaceSummary("Shibuya Sky", "Viewpoint", 4.8, 24_960, "Open-air rooftop deck over the world's busiest crossing."),
            ],
            ["Paris"] =
            [
                new PlaceSummary("Musée d'Orsay", "Museum", 4.7, 91_204, "Impressionist masterworks in a beaux-arts railway station."),
                new PlaceSummary("Sainte-Chapelle", "Landmark", 4.8, 38_550, "Royal chapel with stained-glass walls — best in late-morning sun."),
                new PlaceSummary("Le Comptoir du Relais", "Restaurant", 4.5, 6_410, "Yves Camdeborde's bistro — tiny, no reservations at lunch."),
            ],
            ["NYC"] =
            [
                new PlaceSummary("The High Line", "Park", 4.7, 142_018, "Mile-and-a-half elevated rail park from Gansevoort to 34th."),
                new PlaceSummary("Whitney Museum", "Museum", 4.6, 24_318, "American art with a Hudson-facing terrace at the High Line's south end."),
                new PlaceSummary("Katz's Delicatessen", "Restaurant", 4.5, 41_802, "Lower East Side institution — pastrami on rye, cash for the carver."),
            ],
        };

    public static PlaceSummary[] For(string? destination) =>
        !string.IsNullOrWhiteSpace(destination) && ByDestination.TryGetValue(destination, out var places)
            ? places
            : ByDestination["Tokyo"];
}
