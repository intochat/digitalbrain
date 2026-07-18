using Ino.Domains.Travel.Rfw;

namespace Ino.Domains.Travel.TripPlanner;

/// <summary>
/// Canned events keyed by destination — the shape mirrors tripradar's
/// <c>events</c> GraphQL query (title / date / venue / ticket info). Real
/// integration: <c>FindEventsRequest</c> against tripradar's
/// <c>GetEventsQuery</c>; until that slice lands we hand back a deterministic
/// set so the activity hop can anchor the day plan around real-feeling
/// commitments.
/// </summary>
internal static class MockEventsCorpus
{
    public static IReadOnlyList<EventOption> For(string destination)
    {
        return (destination?.ToLowerInvariant()) switch
        {
            "bali" =>
            [
                new("EV-001",
                    "Balinese Gamelan Night at Ubud Palace",
                    "Sat, Jun 14",
                    "Ubud Palace",
                    "music",
                    "From $20",
                    "Traditional gamelan ensemble in the open-air courtyard. 90 minutes."),
                new("EV-002",
                    "Tegalalang Rice Terrace Sunrise Walk",
                    "Sun, Jun 15",
                    "Tegalalang",
                    "outdoors",
                    "Free",
                    "Guided pre-dawn walk along the iconic terraces. Bring a rain shell just in case."),
                new("EV-003",
                    "Saraswati Temple Lotus Ceremony",
                    "Mon, Jun 16",
                    "Saraswati Temple, Ubud",
                    "culture",
                    "By donation",
                    "Brief blessing ceremony among the lotus ponds; modest dress requested."),
            ],
            "tokyo" =>
            [
                new("EV-101",
                    "TeamLab Planets — Limited Spring Garden",
                    "Wed, Apr 22",
                    "TeamLab Planets, Toyosu",
                    "exhibit",
                    "From $32",
                    "Immersive digital art with seasonal moss garden installation."),
                new("EV-102",
                    "Yomiuri Giants vs. Hanshin Tigers",
                    "Fri, Apr 24",
                    "Tokyo Dome",
                    "sports",
                    "From $28",
                    "Classic Central League rivalry; arrive early for batting practice."),
            ],
            _ =>
            [
                new("EV-900",
                    "Local Festival Night Market",
                    "Throughout the trip",
                    "City center",
                    "food",
                    "Free entry",
                    "Rotating evening market with food stalls and live music."),
            ],
        };
    }
}
