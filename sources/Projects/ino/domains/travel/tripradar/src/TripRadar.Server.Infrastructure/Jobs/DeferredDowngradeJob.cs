using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Jobs;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Comms.Core.Exceptions;

namespace TripRadar.Server.Infrastructure.Jobs;

public class DeferredDowngradeJob(
    IUnitOfWork unitOfWork,
    IPaymentService paymentService,
    IBackgroundJobService backgroundJobService,
    ILogger<DeferredDowngradeJob> logger) : IDeferredDowngradeJob
{
    public async Task ExecuteAsync(long userId, int targetTierId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

            var user = await unitOfWork.UserRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null)
            {
                logger.LogError("User with ID {UserId} not found for deferred downgrade", userId);
                return;
            }

            if (user.TierId <= targetTierId)
            {
                logger.LogError("User {UserId} already at or below target tier {TargetTierId}", userId, targetTierId);
                return;
            }

            var result = await paymentService.ProcessDeferredDowngradeAsync(user, targetTierId, cancellationToken);
            if (result.IsFailure)
                throw new InternalErrorException($"Deferred downgrade failed: {result.Error.Reason}");

            await scope.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during deferred downgrade for user {UserId} to tier {TargetTierId}", userId, targetTierId);
            throw new InternalErrorException($"{Errors.PaymentProcessingFailed.Reason}", ex);
        }
        finally
        {
            await backgroundJobService.OnJobCompletedAsync(userId, cancellationToken);
        }
    }
}
