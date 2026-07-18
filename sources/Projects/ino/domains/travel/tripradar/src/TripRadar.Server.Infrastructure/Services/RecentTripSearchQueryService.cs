using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Repositories.Models;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Infrastructure.Services;

public sealed class RecentTripSearchQueryService(
    IUnitOfWork unitOfWork,
    IEnumerable<IRecentSearchPayloadBuilder> payloadBuilders) : IRecentTripSearchQueryService
{
    public async Task<IReadOnlyList<RecentSearchItemDetails>> GetByUserIdAsync(
        long userId,
        ServiceType serviceType,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var items = await unitOfWork.TripVaultRepository.GetRecentQueryHistoryByDefaultVaultAsync(userId, serviceType, limit, cancellationToken);
        if (items.Count == 0)
        {
            return [];
        }

        var builder = payloadBuilders.FirstOrDefault(candidate => candidate.ServiceType.Id == serviceType.Id);
        if (builder is null)
        {
            return [];
        }

        return await builder.BuildManyAsync(items, cancellationToken);
    }
}
