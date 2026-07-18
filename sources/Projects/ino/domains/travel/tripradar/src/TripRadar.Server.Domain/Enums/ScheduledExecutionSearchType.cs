using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Enums;

public class ScheduledExecutionSearchType(int id, string name) : Enumeration(id, name)
{
    public static readonly ScheduledExecutionSearchType Flights = new(1, nameof(Flights));
    public static readonly ScheduledExecutionSearchType Hotels = new(2, nameof(Hotels));
    public static readonly ScheduledExecutionSearchType Events = new(3, nameof(Events));
    public static readonly ScheduledExecutionSearchType LocalPlaces = new(4, nameof(LocalPlaces));
}
