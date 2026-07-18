using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Policies;

namespace TripRadar.Server.Application.UseCases.TripVaults.Commands.CreateTripVault;

public class CreateTripVaultHandler(IUnitOfWork unitOfWork, ICurrentUserContext currentUserContext)
    : IRequestHandler<CreateTripVaultCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateTripVaultCommand request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();
        if (!PaidTierEligibilityPolicy.IsEligibleForPaidFeatures(user))
        {
            return Result.Failure<Guid>(Errors.InsufficientSubscriptionTier);
        }

        var normalizedName = request.Name.Trim();
        var tripVaultNameExists = await unitOfWork.TripVaultRepository.ExistsByOwnerIdAndNameAsync(
            user.Id,
            normalizedName,
            cancellationToken);

        if (tripVaultNameExists)
        {
            return Result.Failure<Guid>(Errors.TripVaultNameAlreadyExists);
        }

        var tripVault = new TripVault(
            user.Id,
            request.Name,
            request.Description,
            request.StartDate,
            request.EndDate);

        await unitOfWork.TripVaultRepository.CreateAsync(tripVault, cancellationToken);
        return Result.Success(tripVault.UniqueId);
    }
}
