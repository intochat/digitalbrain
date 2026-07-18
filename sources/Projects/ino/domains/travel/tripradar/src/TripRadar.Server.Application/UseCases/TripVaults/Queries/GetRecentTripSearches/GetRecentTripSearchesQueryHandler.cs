using MediatR;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories.Models;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Policies;

namespace TripRadar.Server.Application.UseCases.TripVaults.Queries.GetRecentTripSearches;

public sealed class GetRecentTripSearchesQueryHandler( IRecentTripSearchQueryService recentTripSearchQueryService, ICurrentUserContext currentUserContext) : IRequestHandler<GetRecentTripSearchesQuery, Result<IReadOnlyList<RecentSearchItemDetails>>>
{
    public async Task<Result<IReadOnlyList<RecentSearchItemDetails>>> Handle(GetRecentTripSearchesQuery request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();
        return !PaidTierEligibilityPolicy.IsEligibleForPaidFeatures(user)
            ? Result.Success<IReadOnlyList<RecentSearchItemDetails>>([])
            : Result.Success(await recentTripSearchQueryService.GetByUserIdAsync(user.Id, request.ServiceType, request.Limit, cancellationToken));
    }
}
