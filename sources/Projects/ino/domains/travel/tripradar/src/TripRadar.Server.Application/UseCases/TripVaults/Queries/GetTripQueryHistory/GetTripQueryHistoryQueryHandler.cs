using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Policies;

namespace TripRadar.Server.Application.UseCases.TripVaults.Queries.GetTripQueryHistory;

public class GetTripQueryHistoryQueryHandler(IUnitOfWork unitOfWork, ICurrentUserContext currentUserContext)
    : IRequestHandler<GetTripQueryHistoryQuery, Result<(IEnumerable<TripQueryHistory> Items, int TotalCount)>>
{
    public async Task<Result<(IEnumerable<TripQueryHistory> Items, int TotalCount)>> Handle(GetTripQueryHistoryQuery request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();
        if (!PaidTierEligibilityPolicy.IsEligibleForPaidFeatures(user))
            return Result.Failure<(IEnumerable<TripQueryHistory>, int)>(Errors.InsufficientSubscriptionTier);

        var (items, totalCount, vaultExists, isOwner) = await unitOfWork.TripVaultRepository.GetQueryHistoryAsync(request.TripVaultId, user.Id, request.PageNumber, request.PageSize, cancellationToken);
        if (!vaultExists)
            return Result.Failure<(IEnumerable<TripQueryHistory>, int)>(Errors.TripVaultNotFound);

        return !isOwner ? Result.Failure<(IEnumerable<TripQueryHistory>, int)>(Errors.TripVaultUnauthorizedAccess) : Result.Success((Items: items, TotalCount: totalCount));
    }
}
