using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Policies;

namespace TripRadar.Server.Application.UseCases.TripVaults.Commands.DeleteTripVault;

public class DeleteTripVaultHandler(IUnitOfWork unitOfWork, ICurrentUserContext currentUserContext)
    : IRequestHandler<DeleteTripVaultCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(DeleteTripVaultCommand request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();
        if (!PaidTierEligibilityPolicy.IsEligibleForPaidFeatures(user))
            return Result.Failure<bool>(Errors.InsufficientSubscriptionTier);

        var tripVault = await unitOfWork.TripVaultRepository.GetByUniqueIdForUpdateAsync(request.TripVaultId, cancellationToken);
        if (tripVault is null)
        {
            return Result.Failure<bool>(Errors.TripVaultNotFound);
        }

        if (tripVault.OwnerId != user.Id)
        {
            return Result.Failure<bool>(Errors.TripVaultUnauthorizedAccess);
        }

        await unitOfWork.TripVaultRepository.DeleteAsync(tripVault, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }
}
