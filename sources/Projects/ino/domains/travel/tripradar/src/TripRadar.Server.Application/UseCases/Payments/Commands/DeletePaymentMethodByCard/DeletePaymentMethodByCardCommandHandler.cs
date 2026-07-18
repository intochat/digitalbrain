using MediatR;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Application.Extensions;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.Payments.Commands.DeletePaymentMethodByCard;

public sealed class DeletePaymentMethodByCardCommandHandler(
    IUnitOfWork unitOfWork,
    IStripeGateway stripeGateway,
    ILogger<DeletePaymentMethodByCardCommandHandler> logger) : IRequestHandler<DeletePaymentMethodByCardCommand, Result<DeletePaymentMethodByCardResponseDTO>>
{
    public async Task<Result<DeletePaymentMethodByCardResponseDTO>> Handle(DeletePaymentMethodByCardCommand request, CancellationToken cancellationToken)
    {
        var user = await unitOfWork.UserRepository.GetByUsernameWithSubscriptionAsync(request.Username, cancellationToken);
        if (user is null)
        {
            return Result.Failure<DeletePaymentMethodByCardResponseDTO>(Errors.UserNotFound);
        }

        var userSubscription = user.UserSubscription;
        if (userSubscription?.StripeCustomerId is null)
        {
            return Result.Failure<DeletePaymentMethodByCardResponseDTO>(Errors.PaymentMethodNotFound);
        }

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
                return Result.Failure<DeletePaymentMethodByCardResponseDTO>(Errors.PaymentMethodNotFound);
            case > 1:
                logger.LogWarning("Ambiguous payment method deletion request for user {Username}. Matches={Count}", request.Username, matches.Count);
                return Result.Failure<DeletePaymentMethodByCardResponseDTO>(Errors.PaymentMethodAmbiguous);
        }

        var targetPaymentMethod = matches[0];
        var targetPaymentMethodId = targetPaymentMethod.Id;

        var stripeSubscription = await stripeGateway.GetSubscriptionByCustomerAsync(userSubscription.StripeCustomerId, cancellationToken);
        var hasActiveSubscription = Equals(stripeSubscription.GetStatusEnum(), SubscriptionStatusType.Active) || Equals(stripeSubscription.GetStatusEnum(), SubscriptionStatusType.Trialing);

        var isDefaultPaymentMethod = stripeSubscription?.DefaultPaymentMethodId == targetPaymentMethodId;

        if (hasActiveSubscription && paymentMethods.Count == 1)
        {
            logger.LogWarning("User {Username} attempted to remove last payment method with active subscription", request.Username);
            return Result.Failure<DeletePaymentMethodByCardResponseDTO>(Errors.CannotRemoveLastPaymentMethod);
        }

        if (paymentMethods.Count == 1)
        {
            var hasUnpaidInvoices = await stripeGateway.HasUnpaidInvoicesAsync(userSubscription.StripeCustomerId, cancellationToken);
            if (hasUnpaidInvoices)
            {
                logger.LogWarning("User {Username} attempted to remove last payment method with unpaid invoices", request.Username);
                return Result.Failure<DeletePaymentMethodByCardResponseDTO>(Errors.HasUnpaidInvoices);
            }
        }

        string? newDefaultLast4 = null;
        int? newDefaultExpMonth = null;
        int? newDefaultExpYear = null;

        if (isDefaultPaymentMethod && paymentMethods.Count > 1)
        {
            var newDefault = paymentMethods
                .Where(pm => pm.Id != targetPaymentMethodId)
                .OrderBy(pm => pm.CreatedAt)
                .First();

            await stripeGateway.SetDefaultPaymentMethodAsync(userSubscription.StripeCustomerId, newDefault.Id, cancellationToken);
            var newDefaultPaymentMethodId = newDefault.Id;
            newDefaultLast4 = newDefault.Last4;
            newDefaultExpMonth = newDefault.ExpMonth;
            newDefaultExpYear = newDefault.ExpYear;

            logger.LogInformation("Set new default payment method {NewDefaultId} for user {Username}", newDefaultPaymentMethodId, request.Username);
        }

        await stripeGateway.DetachPaymentMethodAsync(userSubscription.StripeCustomerId, targetPaymentMethodId, cancellationToken);

        logger.LogInformation(
            "Deleted payment method {PaymentMethodId} for user {Username}. Remaining: {RemainingCount}",
            targetPaymentMethodId,
            request.Username,
            paymentMethods.Count - 1);

        return Result.Success(new DeletePaymentMethodByCardResponseDTO
        {
            Message = "Payment method removed successfully",
            NewDefaultPaymentMethodLast4 = newDefaultLast4,
            NewDefaultPaymentMethodExpMonth = newDefaultExpMonth,
            NewDefaultPaymentMethodExpYear = newDefaultExpYear,
            RemainingPaymentMethods = paymentMethods.Count - 1
        });
    }
}
