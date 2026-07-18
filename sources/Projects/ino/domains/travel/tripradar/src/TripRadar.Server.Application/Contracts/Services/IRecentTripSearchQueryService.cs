using TripRadar.Server.Application.Contracts.Repositories.Models;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Contracts.Services;

public interface IRecentTripSearchQueryService
{
    Task<IReadOnlyList<RecentSearchItemDetails>> GetByUserIdAsync(long userId, ServiceType serviceType, int limit, CancellationToken cancellationToken = default);
}
