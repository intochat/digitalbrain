namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

public sealed class FlightFilterState
{
    public HashSet<int> Stops { get; } = [];
    public HashSet<string> Airlines { get; } = [];
    public decimal? MaxPrice { get; set; }
    public DepartureTimeRange? TimeRange { get; set; }

    public bool HasActiveFilters => Stops.Count > 0 || Airlines.Count > 0 || MaxPrice.HasValue || TimeRange.HasValue;

    public void Reset()
    {
        Stops.Clear();
        Airlines.Clear();
        MaxPrice = null;
        TimeRange = null;
    }
}