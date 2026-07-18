using TripRadar.MiniApp.Client.Infrastructure.Models.Common;

namespace TripRadar.MiniApp.Client.Infrastructure.Contracts;

public interface IAirportManager : IManager
{
    Task<List<AirportSuggestion>> SearchAsync(string query);
}