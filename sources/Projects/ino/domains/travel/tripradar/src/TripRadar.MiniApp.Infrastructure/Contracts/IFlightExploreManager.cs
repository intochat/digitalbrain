using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Client.Infrastructure.Contracts;

public interface IFlightExploreManager : IManager
{
    Task<FlightExploreResult?> GetPopularDestinationsAsync(string departureId);
}