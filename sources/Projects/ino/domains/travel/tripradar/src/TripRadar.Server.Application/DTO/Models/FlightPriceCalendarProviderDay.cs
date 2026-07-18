namespace TripRadar.Server.Application.DTO.Models
{
    public sealed record FlightPriceCalendarProviderDay(DateOnly Date, decimal LowestPrice, string Currency);
}