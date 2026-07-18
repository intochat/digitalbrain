using MediatR;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Payments.Commands.UpdateDefaultPaymentMethod;

public sealed class UpdateDefaultPaymentMethodCommandHandler(
    IUnitOfWork unitOfWork,
    IStripeGateway stripeGateway,
    ILogger<UpdateDefaultPaymentMethodCommandHandler> logger)
    : IRequestHandler<UpdateDefaultPaymentMethodCommand, Result<UpdateDefaultPaymentMethodDto>>
{
    public async Task<Result<UpdateDefaultPaymentMethodDto>> Handle(UpdateDefaultPaymentMethodCommand request, CancellationToken cancellationToken)
    {
        var user = await unitOfWork.UserRepository.GetByUsernameWithSubscriptionAsync(request.Username, cancellationToken);
        if (user is null)
            return Result.Failure<UpdateDefaultPaymentMethodDto>(Errors.UserNotFound);

        if (!request.SetAsDefault)
            return Result.Success(new UpdateDefaultPaymentMethodDto { Message = "No changes applied" });

        var userSubscription = user.UserSubscription;
        if (userSubscription?.StripeCustomerId is null)
            return Result.Failure<UpdateDefaultPaymentMethodDto>(Errors.PaymentMethodNotFound);

        var paymentMethods = await stripeGateway.GetPaymentMethodsAsync(userSubscription.StripeCustomerId, cancellationToken);

        var matches = paymentMethods.Where(pm =>
            string.Equals(pm.Last4, request.Last4, StringComparison.OrdinalIgnoreCase) &&
            pm.ExpMonth == request.ExpMonth &&
            pm.ExpYear == request.ExpYear &&
            (string.IsNullOrWhiteSpace(request.Brand) || string.Equals(pm.Brand, request.Brand, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        switch (matches.Count)
        {
            case 0:
                return Result.Failure<UpdateDefaultPaymentMethodDto>(Errors.PaymentMethodNotFound);
            case > 1:
                logger.LogWarning("Ambiguous payment method set-default request for user {Username}. Matches={Count}", request.Username, matches.Count);
                return Result.Failure<UpdateDefaultPaymentMethodDto>(Errors.PaymentMethodAmbiguous);
        }

        var targetPaymentMethod = matches[0];

        await stripeGateway.SetDefaultPaymentMethodAsync(userSubscription.StripeCustomerId, targetPaymentMethod.Id, cancellationToken);

        return Result.Success(new UpdateDefaultPaymentMethodDto
        {
            Message = "Default payment method updated successfully",
            DefaultPaymentMethodLast4 = targetPaymentMethod.Last4,
            DefaultPaymentMethodExpMonth = targetPaymentMethod.ExpMonth,
            DefaultPaymentMethodExpYear = targetPaymentMethod.ExpYear
        });
    }
}
