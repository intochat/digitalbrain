using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Policies;

namespace TripRadar.Server.Application.UseCases.TripVaults.Commands.RemoveTripItem;

public class RemoveTripItemByUniqueIdHandler(IUnitOfWork unitOfWork, ICurrentUserContext currentUserContext)
    : IRequestHandler<RemoveTripItemByUniqueIdCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RemoveTripItemByUniqueIdCommand request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();
        if (!PaidTierEligibilityPolicy.IsEligibleForPaidFeatures(user))
            return Result.Failure<bool>(Errors.InsufficientSubscriptionTier);

        var tripVault = await unitOfWork.TripVaultRepository.GetByUniqueIdWithSingleItemByUniqueIdForUpdateAsync(
            request.TripVaultUniqueId,
            request.ItemUniqueId,
            cancellationToken);

        if (tripVault is null)
        {
            return Result.Failure<bool>(Errors.TripVaultNotFound);
        }

        if (tripVault.OwnerId != user.Id)
        {
            return Result.Failure<bool>(Errors.TripVaultUnauthorizedAccess);
        }

        if (tripVault.QueryHistory.All(item => item.UniqueId != request.ItemUniqueId))
        {
            return Result.Failure<bool>(Errors.TripVaultItemNotFound);
        }

        tripVault.RemoveItem(request.ItemUniqueId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }
}
