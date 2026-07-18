using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Emails;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Infrastructure.Constants;
using TripRadar.Server.Infrastructure.Contracts;
using TripRadar.Server.Infrastructure.Services.Helpers;

namespace TripRadar.Server.Infrastructure.Services.Payments;

public class RefundService(
    IStripeApiProvider stripeApiProvider,
    IUnitOfWork unitOfWork,
    IUserMonthlyTokenCountRepository userMonthlyTokenCountRepository,
    IUserSubscriptionRepository userSubscriptionRepository,
    ITierRepository tierRepository,
    IEmailService emailService,
    ILogger<RefundService> logger) : IRefundService
{
    public async Task<Result<RefundResult>> CreateRefundAsync(
        User user,
        RefundType type,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var validationResult = await ValidateRefundEligibilityAsync(user, cancellationToken);
            if (validationResult.IsFailure)
            {
                return Result.Failure<RefundResult>(validationResult.Error);
            }

            var userSubscription = await userSubscriptionRepository.GetByUserIdAsync(user.Id, cancellationToken);
            if (userSubscription?.StripeSubscriptionId == null)
            {
                return Result.Failure<RefundResult>(Errors.PaymentNotFound);
            }

            var paymentIntentResult = await GetLatestPaymentIntentAsync(userSubscription.StripeSubscriptionId, cancellationToken);
            if (paymentIntentResult.IsFailure)
            {
                return Result.Failure<RefundResult>(paymentIntentResult.Error);
            }

            var refundMetadata = PaymentServiceHelper.BuildRefundMetadata(metadata);
            var stripeReason = PaymentServiceHelper.GetStripeCompatibleRefundReason(type);

            var stripeRefundResult = await stripeApiProvider.CreateRefundAsync(
                paymentIntentResult.Value!,
                null,
                stripeReason,
                refundMetadata,
                cancellationToken);

            await ProcessSuccessfulRefundAsync(user, cancellationToken);

            var refundResult = new RefundResult(
                stripeRefundResult.RefundId,
                stripeRefundResult.PaymentIntentId,
                stripeRefundResult.Amount / 100.0m,
                stripeRefundResult.Currency,
                stripeRefundResult.Status,
                stripeRefundResult.Reason,
                stripeRefundResult.Created,
                stripeRefundResult.Metadata);

            await SendRefundProcessedEmailAsync(user, stripeRefundResult.Amount, refundResult.Currency, type, refundResult.Created, cancellationToken);

            logger.LogInformation(
                "Refund created successfully for user {Username}: RefundId={RefundId}, Amount={Amount}",
                user.Profile.Username,
                refundResult.RefundId,
                refundResult.Amount);

            return Result.Success(refundResult);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error creating refund for user {UserId}", user.Id);
            return Result.Failure<RefundResult>(Errors.PaymentProcessingFailed);
        }
    }

    private async Task<Result> ValidateRefundEligibilityAsync(User user, CancellationToken cancellationToken)
    {
        var userTokenCount = await userMonthlyTokenCountRepository.GetByUserIdAsync(user.Id, cancellationToken);
        if (userTokenCount is null)
        {
            logger.LogError("User token count not found for user {UserId}. Cannot process refund", user.Id);
            return Result.Failure(Errors.UserNotFound);
        }

        var basicTier = await tierRepository.GetByNameAsync(UserTierType.Basic.Name, cancellationToken);
        var basicTierLimit = basicTier?.TokensPerMonthLimit ?? 50m;

        if (userTokenCount.TokensConsumed > basicTierLimit)
        {
            logger.LogWarning(
                "Refund denied for user {Username}: Token usage ({CurrentTokens}) exceeds Basic tier limit ({BasicLimit})",
                user.Profile.Username,
                userTokenCount.TokensConsumed,
                basicTierLimit);
            return Result.Failure(Errors.RefundNotAllowed);
        }

        var userSubscription = await userSubscriptionRepository.GetByUserIdAsync(user.Id, cancellationToken);
        if (userSubscription?.StripeSubscriptionId == null)
        {
            logger.LogWarning("Refund denied for user {Username}: No active Stripe subscription found", user.Profile.Username);
            return Result.Failure(Errors.PaymentNotFound);
        }

        var (subscriptionStatus, _, _) = await stripeApiProvider.GetSubscriptionDetailsAsync(
            userSubscription.StripeSubscriptionId,
            cancellationToken);

        if (!string.Equals(subscriptionStatus, SubscriptionConstants.Status.Active, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Refund denied for user {Username}: Subscription status is {Status}",
                user.Profile.Username,
                subscriptionStatus);
            return Result.Failure(Errors.PaymentNotFound);
        }

        return Result.Success();
    }

    private async Task<Result<string>> GetLatestPaymentIntentAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        var paymentIntentId = await stripeApiProvider.GetLatestPaymentIntentFromSubscriptionAsync(
            subscriptionId,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(paymentIntentId))
        {
            return Result.Success(paymentIntentId);
        }

        logger.LogWarning("No payment intent found for subscription {SubscriptionId}", subscriptionId);
        return Result.Failure<string>(Errors.PaymentNotFound);
    }

    private async Task ProcessSuccessfulRefundAsync(User user, CancellationToken cancellationToken)
    {
        user.UpdateTier(UserTierType.Basic.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task SendRefundProcessedEmailAsync(
        User user,
        int amountInCents,
        string currency,
        RefundType refundType,
        DateTime created,
        CancellationToken cancellationToken)
    {
        try
        {
            var sent = await emailService.SendRefundProcessedAsync(
                user.Profile.Email,
                user.Profile.Username ?? user.Profile.Email,
                amountInCents,
                currency.ToUpperInvariant(),
                refundType.Name,
                created,
                user.Profile.Language?.LanguageCode,
                cancellationToken);

            if (!sent)
            {
                logger.LogWarning(
                    "Refund processed email was not sent for user {UserId}. Check email configuration and EmailService logs.",
                    user.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send refund processed email for user {UserId}", user.Id);
        }
    }
}


