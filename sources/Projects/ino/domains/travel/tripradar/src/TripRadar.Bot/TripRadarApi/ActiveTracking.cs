namespace TripRadar.Bot.TripRadarApi;

public sealed record ActiveTracking(
    Guid ScheduledExecutionId,
    string Username,
    string DepartureAirportCode,
    string DestinationAirportCode,
    DateOnly DepartureDate);
