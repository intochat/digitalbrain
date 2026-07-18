using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Policies;

namespace TripRadar.Server.Application.UseCases.TripVaults.Commands.UpdateTripVault;

public class UpdateTripVaultHandler(IUnitOfWork unitOfWork, ICurrentUserContext currentUserContext) : IRequestHandler<UpdateTripVaultCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(UpdateTripVaultCommand request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();
        if (!PaidTierEligibilityPolicy.IsEligibleForPaidFeatures(user))
        {
            return Result.Failure<Guid>(Errors.InsufficientSubscriptionTier);
        }

        var normalizedName = request.Name.Trim();

        var tripVault = await unitOfWork.TripVaultRepository.GetByUniqueIdForUpdateAsync(request.TripVaultUniqueId, cancellationToken);
        if (tripVault is null)
        {
            return Result.Failure<Guid>(Errors.TripVaultNotFound);
        }

        if (tripVault.OwnerId != user.Id)
        {
            return Result.Failure<Guid>(Errors.TripVaultUnauthorizedAccess);
        }

        var tripVaultNameExists = await unitOfWork.TripVaultRepository.ExistsByOwnerIdAndNameExcludingVaultAsync(user.Id, normalizedName, tripVault.UniqueId, cancellationToken);
        if (tripVaultNameExists)
        {
            return Result.Failure<Guid>(Errors.TripVaultNameAlreadyExists);
        }

        tripVault.Update(request.Name, request.Description, request.StartDate, request.EndDate);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(tripVault.UniqueId);
    }
}
