using Ino.Domains.Travel.Rfw;

namespace Ino.Domains.Travel.TripPlanner;

/// <summary>
/// Activities = places-to-visit enriched with indoor/outdoor classification
/// and a weather hint. The activity hop ranks these against the trip's
/// climatology so a rainy week pushes the spa above the rice terraces.
/// </summary>
internal static class MockActivityCorpus
{
    public static IReadOnlyList<ActivityOption> For(string destination, WeatherClimatology climatology)
    {
        // Pick the canonical destination set, then re-label weather badges
        // based on the climatology so the same activity reads "Rainy day
        // pick" or "Sunny day pick" depending on the trip dates.
        var raw = (destination?.ToLowerInvariant()) switch
        {
            "bali" => BaliActivities(),
            "tokyo" => TokyoActivities(),
            _      => GenericActivities(destination ?? "your destination"),
        };

        return raw.Select(a => a with { WeatherBadge = WeatherBadgeFor(a.IsIndoor, climatology) }).ToArray();
    }

    static IReadOnlyList<ActivityOption> BaliActivities() =>
    [
        new("AC-001", "Tegalalang Rice Terraces",  "Nature",   4.6, IsIndoor: false, WeatherBadge: ""),
        new("AC-002", "Tirta Empul Temple",         "Culture",  4.7, IsIndoor: false, WeatherBadge: ""),
        new("AC-003", "Sacred Monkey Forest",       "Wildlife", 4.5, IsIndoor: false, WeatherBadge: ""),
        new("AC-004", "Agung Rai Museum of Art",    "Museum",   4.6, IsIndoor: true,  WeatherBadge: ""),
        new("AC-005", "Bambu Spa, Ubud",            "Wellness", 4.8, IsIndoor: true,  WeatherBadge: ""),
    ];

    static IReadOnlyList<ActivityOption> TokyoActivities() =>
    [
        new("AC-101", "Senso-ji Temple, Asakusa",   "Culture",  4.6, IsIndoor: false, WeatherBadge: ""),
        new("AC-102", "Tokyo National Museum",      "Museum",   4.7, IsIndoor: true,  WeatherBadge: ""),
        new("AC-103", "Shibuya Crossing + Hachiko", "Sights",   4.5, IsIndoor: false, WeatherBadge: ""),
        new("AC-104", "TeamLab Borderless",         "Exhibit",  4.8, IsIndoor: true,  WeatherBadge: ""),
    ];

    static IReadOnlyList<ActivityOption> GenericActivities(string destination) =>
    [
        new("AC-901", $"Old Town Walk, {destination}", "Sights", 4.4, IsIndoor: false, WeatherBadge: ""),
        new("AC-902", $"Local Museum, {destination}",  "Museum", 4.3, IsIndoor: true,  WeatherBadge: ""),
    ];

    static string WeatherBadgeFor(bool isIndoor, WeatherClimatology climatology)
    {
        // Wet season — push indoor picks; dry season — outdoor picks shine.
        // "Shoulder" is the catch-all that doesn't bias either way.
        return (climatology.Season, isIndoor) switch
        {
            ("wet",      true)  => "Rainy day pick",
            ("wet",      false) => "Bring a rain shell",
            ("dry",      false) => "Sunny day pick",
            ("dry",      true)  => "Cool off indoors",
            _                   => "All weather",
        };
    }
}
