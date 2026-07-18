namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

public sealed record FlightBundle(
    FlightOption Outbound,
    FlightOption Return,
    decimal TotalPrice,
    int TotalDuration,
    FlightBundleTag Tag
)
{
    public static List<FlightBundle> Create(
        IReadOnlyList<(FlightOption Outbound, FlightOption Return)> pairs)
    {
        if (pairs.Count == 0) return [];

        var bundles = pairs
            .Select(p => new FlightBundle(
                p.Outbound,
                p.Return,
                p.Return.Price,
                p.Outbound.TotalDuration + p.Return.TotalDuration,
                FlightBundleTag.None))
            .ToList();

        return AssignTags(bundles);
    }

    private static List<FlightBundle> AssignTags(List<FlightBundle> bundles)
    {
        if (bundles.Count == 0) return bundles;

        var tagged = new HashSet<int>();

        // First bundle inherits SerpAPI's "best" ranking
        tagged.Add(0);
        bundles[0] = bundles[0] with { Tag = FlightBundleTag.Best };

        // Cheapest by total price
        var cheapestIdx = bundles
            .Select((b, i) => (b, i))
            .OrderBy(x => x.b.TotalPrice)
            .First().i;

        if (!tagged.Contains(cheapestIdx))
        {
            tagged.Add(cheapestIdx);
            bundles[cheapestIdx] = bundles[cheapestIdx] with { Tag = FlightBundleTag.Cheapest };
        }

        // Fastest by total duration
        var fastestIdx = bundles
            .Select((b, i) => (b, i))
            .OrderBy(x => x.b.TotalDuration)
            .First().i;

        if (!tagged.Contains(fastestIdx))
        {
            bundles[fastestIdx] = bundles[fastestIdx] with { Tag = FlightBundleTag.Fastest };
        }

        return bundles;
    }
}