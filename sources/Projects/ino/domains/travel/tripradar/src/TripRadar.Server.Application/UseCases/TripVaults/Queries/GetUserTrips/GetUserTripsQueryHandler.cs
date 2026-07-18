using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Policies;

namespace TripRadar.Server.Application.UseCases.TripVaults.Queries.GetUserTrips;

public class GetUserTripsQueryHandler(IUnitOfWork unitOfWork, ICurrentUserContext currentUserContext) : IRequestHandler<GetUserTripsQuery, Result<IEnumerable<TripVault>>>
{
    public async Task<Result<IEnumerable<TripVault>>> Handle(GetUserTripsQuery request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();
        return !PaidTierEligibilityPolicy.IsEligibleForPaidFeatures(user)
            ? Result.Failure<IEnumerable<TripVault>>(Errors.InsufficientSubscriptionTier)
            : Result.Success(await unitOfWork.TripVaultRepository.GetByUserIdAsync(user.Id, limit: 100, cancellationToken));
    }
}
