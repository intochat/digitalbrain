using MediatR;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Payments.Commands.CancelSubscription;

public class CancelSubscriptionHandler(
    IPaymentService paymentService,
    ICurrentUserContext currentUserContext,
    IBackgroundJobService backgroundJobService) : IRequestHandler<CancelSubscriptionCommand, Result>
{
    public async Task<Result> Handle(CancelSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();

        var cancellationResult = await paymentService.CancelSubscriptionAsync(user, cancellationToken);
        if (cancellationResult.IsFailure)
        {
            return cancellationResult;
        }

        backgroundJobService.EnqueueSubscriptionCancellationEmail(user.Id, request.CancellationReason);

        return Result.Success();
    }
}
