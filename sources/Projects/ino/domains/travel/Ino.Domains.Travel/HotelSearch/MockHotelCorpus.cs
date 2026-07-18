using Ino.Domains.Travel.Rfw;

namespace Ino.Domains.Travel.HotelSearch;

internal static class MockHotelCorpus
{
    public static IReadOnlyList<HotelOption> For(string prompt) => new[]
    {
        new HotelOption("H-001", "Bambu Indah",      280m, 4.7, "Ubud"),
        new HotelOption("H-002", "Como Uma Ubud",    420m, 4.8, "Ubud"),
        new HotelOption("H-003", "Four Seasons Sayan", 550m, 4.9, "Sayan"),
    };
}
