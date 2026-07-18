using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Infrastructure.Services.Payments;

public class SubscriptionStateService(
    IUnitOfWork unitOfWork,
    ILogger<SubscriptionStateService> logger) : ISubscriptionStateService
{
    /// <inheritdoc />
    public async Task<SubscriptionChangeType> DetermineChangeTypeAsync(
        User user,
        Price newPrice,
        UserSubscription subscription,
        CancellationToken ct = default)
    {
        if (!subscription.SubscriptionExpirationTime.HasValue)
        {
            return SubscriptionChangeType.NewSubscription;
        }

        var currentMonthlyPrice = await unitOfWork.PriceRepository.GetByTierIdAndBillingPeriodAsync(user.TierId, 1, ct);
        var currentYearlyPrice = await unitOfWork.PriceRepository.GetByTierIdAndBillingPeriodAsync(user.TierId, 2, ct);

        var newMonthlyPrice = await unitOfWork.PriceRepository.GetByTierIdAndBillingPeriodAsync(newPrice.TierId, 1, ct);
        var newYearlyPrice = await unitOfWork.PriceRepository.GetByTierIdAndBillingPeriodAsync(newPrice.TierId, 2, ct);

        if (user.TierId.Equals(newPrice.TierId))
        {
            if (currentMonthlyPrice != null && newYearlyPrice != null && newPrice.StripeId == newYearlyPrice.StripeId)
            {
                return SubscriptionChangeType.MonthlyToYearly;
            }

            if (currentYearlyPrice != null && newMonthlyPrice != null && newPrice.StripeId == newMonthlyPrice.StripeId)
            {
                return SubscriptionChangeType.YearlyToMonthly;
            }

            return SubscriptionChangeType.SameTierDifferentBilling;
        }

        if (newPrice.TierId > user.TierId)
        {
            return SubscriptionChangeType.TierUpgrade;
        }

        return newPrice.TierId < user.TierId
            ? SubscriptionChangeType.TierDowngrade
            : SubscriptionChangeType.RegularUpdate;
    }

    /// <inheritdoc />
    public void ApplyBillingTransition(
        User user,
        UserSubscription subscription,
        SubscriptionChangeType changeType,
        DateTime newExpirationTime)
    {
        var utcExpirationTime = DateTime.SpecifyKind(newExpirationTime, DateTimeKind.Utc);

        if (Equals(changeType, SubscriptionChangeType.MonthlyToYearly))
        {
            subscription.ExtendSubscription(utcExpirationTime);
            logger.LogInformation("User {UserId} subscription extended for monthly to yearly transition", user.Id);
        }
        else if (Equals(changeType, SubscriptionChangeType.YearlyToMonthly))
        {
            HandleYearlyToMonthlyTransition(user, subscription, utcExpirationTime);
        }
        else
        {
            subscription.UpdateSubscriptionExpirationTime(utcExpirationTime);
        }
    }

    private void HandleYearlyToMonthlyTransition(User user, UserSubscription subscription, DateTime newExpirationTime)
    {
        if (!subscription.SubscriptionExpirationTime.HasValue)
        {
            subscription.UpdateSubscriptionExpirationTime(newExpirationTime);
            return;
        }

        var currentExpiration = subscription.SubscriptionExpirationTime.Value;
        var now = DateTime.UtcNow;
        var remainingDays = (currentExpiration - now).TotalDays;

        if (remainingDays > 30)
        {
            var firstMonthEnd = now.AddDays(30);
            subscription.UpdateSubscriptionExpirationTime(firstMonthEnd);
            logger.LogInformation("User {UserId} has {RemainingDays} days of yearly subscription remaining, credited for future months", user.Id, remainingDays);
        }
        else
        {
            subscription.UpdateSubscriptionExpirationTime(
                currentExpiration > newExpirationTime ? currentExpiration : newExpirationTime);
        }
    }
}
