using TripRadar.Server.API.Contracts.Responses.Get;

namespace TripRadar.Server.API.Contracts;

public interface ITripQueryHistorySummaryExpander
{
    Task ExpandAsync(List<TripItemResponse> items, CancellationToken cancellationToken = default);
}
