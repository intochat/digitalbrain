using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Entities;

public class Airport : Entity<int>
{
    private Airport() { }

    public new int Id { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string City { get; private set; } = null!;

    public string Country { get; private set; } = null!;

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public string? AirportType { get; private set; }

    public string? SearchAliases { get; private set; }

    // Use only in EF
    private ICollection<ScheduledFlightQuery> DepartureScheduledFlightQueries { get; set; } = new List<ScheduledFlightQuery>();

    // Use only in EF
    private ICollection<ScheduledFlightQuery> DestinationScheduledFlightQueries { get; set; } = new List<ScheduledFlightQuery>();
}