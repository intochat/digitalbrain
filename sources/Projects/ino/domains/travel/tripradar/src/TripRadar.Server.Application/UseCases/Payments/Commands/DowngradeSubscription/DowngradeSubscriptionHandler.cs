using MediatR;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Payments.Commands.DowngradeSubscription;

public class DowngradeSubscriptionHandler(
    IPaymentService paymentService,
    ICurrentUserContext currentUserContext,
    IBackgroundJobService backgroundJobService) : IRequestHandler<DowngradeSubscriptionCommand, Result>
{
    public async Task<Result> Handle(DowngradeSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();

        var downgradeResult = await paymentService.DowngradeSubscriptionAsync(user, request.TargetTierId, request.BillingPeriodId, cancellationToken);
        if (downgradeResult.IsFailure)
        {
            return downgradeResult;
        }

        backgroundJobService.EnqueueSubscriptionDowngradeScheduledEmail(user.Id, request.TargetTierId);

        return downgradeResult;
    }
}
