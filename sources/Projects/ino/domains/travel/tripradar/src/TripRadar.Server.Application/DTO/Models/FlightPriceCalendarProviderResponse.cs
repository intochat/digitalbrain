namespace TripRadar.Server.Application.DTO.Models
{
    public sealed record FlightPriceCalendarProviderResponse(IReadOnlyList<FlightPriceCalendarProviderDay> Days);
}