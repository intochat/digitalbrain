using TripRadar.Server.Application.Contracts.Repositories.Models;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Contracts.Services;

public interface IRecentSearchPayloadBuilder
{
    ServiceType ServiceType { get; }

    Task<IReadOnlyList<RecentSearchItemDetails>> BuildManyAsync(IReadOnlyList<TripQueryHistory> items, CancellationToken cancellationToken = default);
}
