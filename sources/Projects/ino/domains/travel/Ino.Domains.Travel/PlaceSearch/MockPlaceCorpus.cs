using Ino.Domains.Travel.Rfw;

namespace Ino.Domains.Travel.PlaceSearch;

internal static class MockPlaceCorpus
{
    public static IReadOnlyList<PlaceOption> For(string prompt) => new[]
    {
        new PlaceOption("P-001", "Tegalalang Rice Terraces", "Nature",  4.6),
        new PlaceOption("P-002", "Sacred Monkey Forest",     "Wildlife", 4.5),
        new PlaceOption("P-003", "Tirta Empul Temple",       "Culture",  4.7),
    };
}
