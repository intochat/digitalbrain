using System.Text.RegularExpressions;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

public static partial class FlightFilterExtensions
{
    public static List<FlightOption> ApplyFilters(this IEnumerable<FlightOption> flights, FlightFilterState filters)
    {
        var result = flights.AsEnumerable();

        if (filters.Stops.Count > 0)
            result = result.Where(f => filters.Stops.Contains(f.Flights.Count - 1));

        if (filters.Airlines.Count > 0)
            result = result.Where(f => f.Flights.Any(s => s.Airline is not null && filters.Airlines.Contains(s.Airline)));

        if (filters.MaxPrice.HasValue)
            result = result.Where(f => f.Price <= filters.MaxPrice.Value);

        if (filters.TimeRange.HasValue)
            result = result.Where(f => MatchesTimeRange(f, filters.TimeRange.Value));

        return result.ToList();
    }

    public static List<FlightOption> SortPaired(this List<FlightOption> flights, PairedSortOrder order) => order switch
    {
        PairedSortOrder.Cheapest => [.. flights.OrderBy(f => f.Price)],
        PairedSortOrder.Fastest => [.. flights.OrderBy(f => f.TotalDuration)],
        _ => flights
    };

    public static List<FlightBundle> SortBundles(this List<FlightBundle> bundles, PairedSortOrder order) => order switch
    {
        PairedSortOrder.Cheapest => [.. bundles.OrderBy(b => b.TotalPrice)],
        PairedSortOrder.Fastest => [.. bundles.OrderBy(b => b.TotalDuration)],
        _ => bundles
    };

    public static HashSet<string> ExtractAirlines(this IEnumerable<FlightOption> flights) =>
        flights
            .SelectMany(f => f.Flights)
            .Select(s => s.Airline)
            .Where(a => !string.IsNullOrEmpty(a))
            .ToHashSet()!;

    public static (decimal Min, decimal Max) GetPriceRange(this IEnumerable<FlightOption> flights)
    {
        var prices = flights.Select(f => f.Price).ToList();
        return prices.Count > 0 ? (prices.Min(), prices.Max()) : (0, 0);
    }

    private static bool MatchesTimeRange(FlightOption flight, DepartureTimeRange range)
    {
        var firstSegment = flight.Flights.FirstOrDefault();
        if (firstSegment?.DepartureAirport.Time is not { } timeStr)
            return true;

        if (!TryParseHour(timeStr, out var hour))
            return true;

        return range switch
        {
            DepartureTimeRange.Morning => hour >= 6 && hour < 12,
            DepartureTimeRange.Afternoon => hour >= 12 && hour < 18,
            DepartureTimeRange.Evening => hour >= 18,
            DepartureTimeRange.Night => hour < 6,
            _ => true
        };
    }

    private static bool TryParseHour(string timeStr, out int hour)
    {
        hour = 0;
        var match = TimePattern().Match(timeStr);
        if (!match.Success) return false;

        hour = int.Parse(match.Groups[1].Value);

        if (timeStr.Contains("PM", StringComparison.OrdinalIgnoreCase) && hour < 12)
            hour += 12;
        else if (timeStr.Contains("AM", StringComparison.OrdinalIgnoreCase) && hour == 12)
            hour = 0;

        return true;
    }

    [GeneratedRegex(@"(\d{1,2}):(\d{2})")]
    private static partial Regex TimePattern();
}