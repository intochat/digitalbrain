using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Infrastructure.Constants;
using TripRadar.Server.Infrastructure.Settings;
using DomainDiscountType = TripRadar.Server.Domain.Enums.DiscountType;

namespace TripRadar.Server.Infrastructure.Services.Payments;

public class PromoCodeService(
    IUnitOfWork unitOfWork,
    IPromoCodeValidationService promoCodeValidationService,
    IOptions<PaymentSettings> paymentSettings,
    ILogger<PromoCodeService> logger) : IPromoCodeService
{
    private readonly PaymentSettings _paymentSettings = paymentSettings.Value;

    public async Task<Result<(PromoCode PromoCode, string StripeCouponId)>> ValidateAndCreateStripeCouponAsync(
        string promoCode,
        User user,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var promoCodeEntity = await unitOfWork.PromoCodeRepository.GetByCodeAsync(promoCode, cancellationToken);
            if (promoCodeEntity is null)
            {
                return Result.Failure<(PromoCode, string)>(Errors.PromoCodeNotFound);
            }

            var validationResult = await promoCodeValidationService.ValidatePromoCodeForUserAsync(
                promoCodeEntity,
                user.Id,
                cancellationToken);

            if (validationResult.IsFailure)
            {
                return Result.Failure<(PromoCode, string)>(validationResult.Error);
            }

            var couponOptions = BuildCouponOptions(promoCodeEntity, promoCode, user.Id);
            var stripeCoupon = await CreateStripeCouponAsync(couponOptions, cancellationToken);

            LogCouponCreation(stripeCoupon.Id, promoCode, promoCodeEntity);

            return Result.Success((promoCodeEntity, stripeCoupon.Id));
        }
        catch (StripeException ex)
        {
            logger.LogError(ex, "Stripe error while creating coupon for promo code {PromoCode}: {Error}", promoCode, ex.Message);
            return Result.Failure<(PromoCode, string)>(
                Errors.PaymentProcessingFailed with { Reason = $"Failed to create discount coupon: {ex.Message}" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error validating promo code {PromoCode} for user {UserId}", promoCode, user.Id);
            return Result.Failure<(PromoCode, string)>(Errors.InternalServerError with { Reason = ex.Message });
        }
    }

    private CouponCreateOptions BuildCouponOptions(PromoCode promoCodeEntity, string promoCode, long userId)
    {
        var couponOptions = new CouponCreateOptions
        {
            Duration = StripeConstants.CouponDuration.Once,
            Name = $"Promo Code: {promoCode}",
            Metadata = new Dictionary<string, string>
            {
                { StripeConstants.Metadata.PromoCode, promoCode },
                { StripeConstants.Metadata.PromoCodeId, promoCodeEntity.Id.ToString(CultureInfo.InvariantCulture) },
                {
                    StripeConstants.Metadata.DiscountType,
                    promoCodeEntity.DiscountTypeId == DomainDiscountType.Percentage.Id
                        ? StripeConstants.DiscountType.Percentage
                        : StripeConstants.DiscountType.Fixed
                },
                {
                    StripeConstants.Metadata.DiscountValue,
                    promoCodeEntity.DiscountValue.ToString("F2", CultureInfo.InvariantCulture)
                },
                { StripeConstants.Metadata.UserId, userId.ToString(CultureInfo.InvariantCulture) }
            }
        };

        if (promoCodeEntity.DiscountTypeId == DomainDiscountType.Percentage.Id)
        {
            couponOptions.PercentOff = promoCodeEntity.DiscountValue;
        }
        else
        {
            couponOptions.AmountOff = (long)(promoCodeEntity.DiscountValue * 100);
            couponOptions.Currency = _paymentSettings.Stripe.Currency;
        }

        return couponOptions;
    }

    private static async Task<Coupon> CreateStripeCouponAsync(
        CouponCreateOptions options,
        CancellationToken cancellationToken)
    {
        var couponService = new CouponService();
        return await couponService.CreateAsync(options, cancellationToken: cancellationToken);
    }

    private void LogCouponCreation(string couponId, string promoCode, PromoCode promoCodeEntity)
    {
        var discountInfo = promoCodeEntity.DiscountTypeId == DomainDiscountType.Percentage.Id
            ? $"{promoCodeEntity.DiscountValue}%"
            : $"{_paymentSettings.Stripe.Currency} {promoCodeEntity.DiscountValue.ToString("F2", CultureInfo.InvariantCulture)}";

        logger.LogInformation("Stripe coupon {CouponId} created for promo code {PromoCode} with discount: {DiscountInfo}", couponId, promoCode, discountInfo);
    }
}
