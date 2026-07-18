using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Policies;

namespace TripRadar.Server.Application.UseCases.TripVaults.Queries.GetTripDetails;

public class GetTripDetailsQueryHandler(IUnitOfWork unitOfWork, ICurrentUserContext currentUserContext)
    : IRequestHandler<GetTripDetailsQuery, Result<TripVault>>
{
    public async Task<Result<TripVault>> Handle(GetTripDetailsQuery request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();
        if (!PaidTierEligibilityPolicy.IsEligibleForPaidFeatures(user))
            return Result.Failure<TripVault>(Errors.InsufficientSubscriptionTier);

        var tripVault = await unitOfWork.TripVaultRepository.GetWithItemsAsync(request.TripVaultId, cancellationToken);
        if (tripVault is null)
        {
            return Result.Failure<TripVault>(Errors.TripVaultNotFound);
        }

        return tripVault.OwnerId != user.Id ? Result.Failure<TripVault>(Errors.TripVaultUnauthorizedAccess) : Result.Success(tripVault);
    }
}
