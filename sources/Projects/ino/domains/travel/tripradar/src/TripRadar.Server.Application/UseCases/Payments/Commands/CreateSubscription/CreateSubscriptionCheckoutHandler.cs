using MediatR;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Payments.Commands.CreateSubscription;

public class CreateSubscriptionCheckoutHandler(
    IPaymentService paymentService,
    ICurrentUserContext currentUserContext) : IRequestHandler<CreateSubscriptionCheckoutCommand, Result<SubscriptionCheckoutDto>>
{
    public async Task<Result<SubscriptionCheckoutDto>> Handle(CreateSubscriptionCheckoutCommand request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();
        return await paymentService.CreateSubscriptionCheckoutAsync(user, request.TargetTierId, request.BillingPeriodId, request.PromoCode, cancellationToken);
    }
}
