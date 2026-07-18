using System.Globalization;
using Microsoft.Extensions.Logging;
using Stripe;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.Errors;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Infrastructure.Constants;
using DomainPrice = TripRadar.Server.Domain.Aggregates.Price;
using DomainPromoCode = TripRadar.Server.Domain.Aggregates.PromoCode;

namespace TripRadar.Server.Infrastructure.Services.Payments;

public class SubscriptionCheckoutService(
    IStripeGateway stripeGateway,
    IUnitOfWork unitOfWork,
    IUserSubscriptionRepository userSubscriptionRepository,
    IPromoCodeService promoCodeService,
    ILogger<SubscriptionCheckoutService> logger)
    : ISubscriptionCheckoutService
{
    public async Task<Result<SubscriptionCheckoutDto>> CreateSubscriptionCheckoutAsync(User user, int targetTierId, int billingPeriodId, string? promoCode = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var checkoutContext = await PrepareCheckoutContextAsync(user, targetTierId, billingPeriodId, promoCode, cancellationToken);
            if (checkoutContext.Error is not null)
                return Result.Failure<SubscriptionCheckoutDto>(checkoutContext.Error);

            var subscriptionMetadata = CreateSubscriptionMetadata(checkoutContext.ValidatedPromoCode, user.Id);
            var checkout = await stripeGateway.CreateSubscriptionCheckoutAsync(
                checkoutContext.CustomerId!,
                checkoutContext.Price!.StripeId!,
                checkoutContext.StripeCouponId,
                subscriptionMetadata,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(checkout.ClientSecret))
            {
                logger.LogWarning(
                    "Stripe checkout did not return a client secret for user {UserId}, customer {CustomerId}, tier {TierId}",
                    user.Id,
                    checkoutContext.CustomerId,
                    targetTierId);
                return Result.Failure<SubscriptionCheckoutDto>(
                    Errors.PaymentInitiationFailed with { Reason = "Stripe did not return a payment client secret for checkout." });
            }

            if (checkoutContext.NeedsCustomerCreation)
                await PersistStripeCustomerIdAsync(checkoutContext.UserSubscription!, checkoutContext.CustomerId!, cancellationToken);

            return Result.Success(new SubscriptionCheckoutDto
            {
                ClientSecret = checkout.ClientSecret,
                Currency = string.IsNullOrWhiteSpace(checkout.Currency)
                    ? checkoutContext.Price.Currency.CurrencyCode
                    : checkout.Currency.ToUpperInvariant(),
                AmountSubtotal = ConvertCentsToAmount(checkout.AmountSubtotal),
                AmountDiscount = ConvertCentsToAmount(checkout.AmountDiscount),
                AmountTotal = ConvertCentsToAmount(checkout.AmountTotal),
                PromoCode = checkoutContext.ValidatedPromoCode?.Code
            });
        }
        catch (StripeException ex)
        {
            logger.LogError(ex, "Stripe API error during checkout creation for user {UserId} to tier {TargetTierId}. Type: {ErrorType}, Code: {ErrorCode}, HttpStatus: {HttpStatus}", user.Id, targetTierId, ex.StripeError?.Type, ex.StripeError?.Code, ex.HttpStatusCode);

            return ex.StripeError?.Type switch
            {
                "authentication_error" => Result.Failure<SubscriptionCheckoutDto>(Errors.StripeAuthenticationFailed),
                "api_connection_error" => Result.Failure<SubscriptionCheckoutDto>(Errors.StripeApiConnectionFailed),
                "invalid_request_error" => Result.Failure<SubscriptionCheckoutDto>(Errors.StripeInvalidRequestFailed),
                "card_error" => Result.Failure<SubscriptionCheckoutDto>(Errors.StripeInvalidRequestFailed),
                _ => Result.Failure<SubscriptionCheckoutDto>(Errors.StripeCheckoutSessionCreationFailed)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during checkout creation for user {UserId} to tier {TargetTierId} with billing period {BillingPeriodId}. Exception: {ExceptionMessage}", user.Id, targetTierId, billingPeriodId, ex.Message);
            return Result.Failure<SubscriptionCheckoutDto>(Errors.PaymentInitiationFailed with { Reason = ex.Message });
        }
    }

    public async Task<Result<string>> CreateSetupIntentAsync(User user, CancellationToken cancellationToken = default)
    {
        try
        {
            var userSubscription = await GetOrCreateUserSubscriptionAsync(user, cancellationToken);
            var needsCustomerCreation = string.IsNullOrWhiteSpace(userSubscription.StripeCustomerId);

            string customerId;
            if (needsCustomerCreation)
            {
                customerId = await stripeGateway.CreateCustomerAsync(user.Profile.Email, user.Profile.Username ?? user.Profile.Email, cancellationToken);

                await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);
                userSubscription.UpdateStripeCustomerId(customerId);
                await userSubscriptionRepository.UpdateAsync(userSubscription, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                await scope.CommitAsync(cancellationToken);
            }
            else
                customerId = userSubscription.StripeCustomerId!;

            var clientSecret = await stripeGateway.CreateSetupIntentAsync(customerId, cancellationToken);
            return Result.Success(clientSecret);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error creating setup intent for user {UserId}. Exception: {ExceptionMessage}", user.Id, ex.Message);
            return Result.Failure<string>(Errors.PaymentInitiationFailed with { Reason = ex.Message });
        }
    }

    private async Task<UserSubscription> GetOrCreateUserSubscriptionAsync(User user, CancellationToken cancellationToken)
    {
        var existingSubscription = await userSubscriptionRepository.GetByUserIdAsync(user.Id, cancellationToken);
        if (existingSubscription is not null)
            return existingSubscription;

        var newSubscription = new UserSubscription(user);
        var createdSubscription = await userSubscriptionRepository.CreateAsync(newSubscription, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return createdSubscription;
    }

    private async Task<CheckoutContext> PrepareCheckoutContextAsync(User user, int targetTierId, int billingPeriodId, string? promoCode, CancellationToken cancellationToken)
    {
        if (user.TierId.Equals(targetTierId))
            return CheckoutContext.Failure(Errors.SameTierUpgrade);

        var price = await unitOfWork.PriceRepository.GetByTierIdAndBillingPeriodAsync(targetTierId, billingPeriodId, cancellationToken);
        if (price is null || string.IsNullOrWhiteSpace(price.StripeId))
            return CheckoutContext.Failure(Errors.TierPriceNotFound);

        DomainPromoCode? validatedPromoCode = null;
        string? stripeCouponId = null;
        if (!string.IsNullOrWhiteSpace(promoCode))
        {
            var promoCodeResult = await promoCodeService.ValidateAndCreateStripeCouponAsync(promoCode, user, cancellationToken);
            if (promoCodeResult.IsFailure)
                return CheckoutContext.Failure(promoCodeResult.Error);

            (validatedPromoCode, stripeCouponId) = promoCodeResult.Value;
            logger.LogInformation("Promo code {PromoCode} validated and Stripe coupon {CouponId} created for user {UserId}", promoCode, stripeCouponId, user.Id);
        }

        var userSubscription = await GetOrCreateUserSubscriptionAsync(user, cancellationToken);
        var needsCustomerCreation = string.IsNullOrWhiteSpace(userSubscription.StripeCustomerId);
        var customerId = needsCustomerCreation
            ? await stripeGateway.CreateCustomerAsync(user.Profile.Email, user.Profile.Username ?? user.Profile.Email, cancellationToken)
            : userSubscription.StripeCustomerId!;

        return new CheckoutContext(price, userSubscription, customerId, needsCustomerCreation, validatedPromoCode, stripeCouponId, null);
    }

    private async Task PersistStripeCustomerIdAsync(UserSubscription userSubscription, string customerId, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);
        userSubscription.UpdateStripeCustomerId(customerId);
        await userSubscriptionRepository.UpdateAsync(userSubscription, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
    }

    private static Dictionary<string, string>? CreateSubscriptionMetadata(DomainPromoCode? promoCode, long userId)
    {
        if (promoCode is null)
            return null;

        return new Dictionary<string, string>
        {
            { StripeConstants.Metadata.PromoCode, promoCode.Code },
            { StripeConstants.Metadata.PromoCodeId, promoCode.Id.ToString(CultureInfo.InvariantCulture) },
            { StripeConstants.Metadata.UserId, userId.ToString(CultureInfo.InvariantCulture) }
        };
    }

    private static decimal ConvertCentsToAmount(long amountInCents) => amountInCents / 100m;

    private sealed record CheckoutContext(
        DomainPrice? Price,
        UserSubscription? UserSubscription,
        string? CustomerId,
        bool NeedsCustomerCreation,
        DomainPromoCode? ValidatedPromoCode,
        string? StripeCouponId,
        Error? Error)
    {
        public static CheckoutContext Failure(Error error) => new(null, null, null, false, null, null, error);
    }
}