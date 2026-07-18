using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Comms.Core.Events;

namespace TripRadar.Server.Infrastructure.Models;

public static class Events
{
    [Topic("Events")]
    public class EventScheduledQuery : PublishableEvent;

    [Topic("Flights")]
    public class FlightScheduledQuery : PublishableEvent;

    [Topic("Hotels")]
    public class HotelScheduledQuery : PublishableEvent;

    [Topic("LocalPlaces")]
    public class LocalPlacesScheduledQuery : PublishableEvent;
}
