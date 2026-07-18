namespace TripRadar.Server.Application.DTO.Models;

public sealed record FlightPriceCalendarProviderRequest(string DepartureId, string ArrivalId, int Year, int Month, string Currency, int? TripLengthDays = null);