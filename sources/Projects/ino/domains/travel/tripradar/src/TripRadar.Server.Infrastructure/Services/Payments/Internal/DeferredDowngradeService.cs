using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Services.Payments.Internal;

public sealed class DeferredDowngradeService(
    IUnitOfWork unitOfWork,
    IUserSubscriptionRepository userSubscriptionRepository,
    IBackgroundJobService backgroundJobService)
{
    public async Task<Result> ScheduleAsync(User user, UserSubscription userSubscription, int targetTierId, DateTime expirationTime, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

        await backgroundJobService.CancelDeferredDowngradeAsync(user.Id, cancellationToken);
        userSubscription.UpdateDeferredDowngrade(expirationTime, targetTierId);
        await userSubscriptionRepository.UpdateAsync(userSubscription, cancellationToken);
        await backgroundJobService.ScheduleDeferredDowngradeAsync(user.Id, targetTierId, expirationTime, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return Result.Success();
    }
}
