using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Client.Infrastructure.Contracts;

public interface IFlightManager : IManager
{
    Task<FlightSearchResult?> SearchAsync(FlightSearchParams p, string? departureToken = null, CancellationToken ct = default);
    Task<FlightBookingResponse?> GetBookingAsync(string bookingToken, FlightSearchParams searchParams);
}