using AutoMapper;
using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Application.UseCases.Payments.Queries.GetUserSubscription;

public class GetUserSubscriptionQueryHandler(
    IStripeGateway stripeGateway,
    IMapper mapper,
    ICurrentUserContext currentUserContext,
    IPriceRepository priceRepository,
    IUserSubscriptionRepository userSubscriptionRepository) : IRequestHandler<GetUserSubscriptionQuery, Result<UserSubscriptionDTO>>
{
    public async Task<Result<UserSubscriptionDTO>> Handle(GetUserSubscriptionQuery request, CancellationToken cancellationToken)
    {
        User user = currentUserContext.GetRequiredUser();

        UserSubscription? userSubscription = await userSubscriptionRepository.GetByUserIdAsync(user.Id, cancellationToken) ?? user.UserSubscription;
        if (userSubscription?.StripeCustomerId is null)
        {
            return Result.Failure<UserSubscriptionDTO>(Errors.SubscriptionNotFound);
        }

        StripeSubscriptionInfo? stripeSubscription = null;
        if (!string.IsNullOrWhiteSpace(userSubscription.StripeSubscriptionId))
        {
            stripeSubscription = await stripeGateway.GetSubscriptionByIdAsync(userSubscription.StripeSubscriptionId, cancellationToken);
        }

        stripeSubscription ??= await stripeGateway.GetSubscriptionByCustomerAsync(userSubscription.StripeCustomerId, cancellationToken);
        if (stripeSubscription is null)
        {
            return Result.Failure<UserSubscriptionDTO>(Errors.SubscriptionNotFound);
        }

        UserSubscriptionDTO? dto = mapper.Map<UserSubscriptionDTO>(stripeSubscription);
        dto.TierType = user.Tier.Name;
        dto.PriceAmount = await ResolveCurrentPriceAmountAsync(user.TierId, stripeSubscription.BillingPeriod, dto.PriceAmount, cancellationToken);
        dto.NextInvoiceDate = !stripeSubscription.CancelAtPeriodEnd ? stripeSubscription.CurrentPeriodEnd : null;
        dto.PendingTierType = ResolvePendingTierType(userSubscription.PendingTierId);
        dto.PendingTierEffectiveDate = dto.PendingTierType is null ? null : stripeSubscription.CurrentPeriodEnd;

        return Result.Success(dto);
    }

    private async ValueTask<int> ResolveCurrentPriceAmountAsync(int currentTierId, string? billingPeriod, int fallbackPriceAmount, CancellationToken cancellationToken)
    {
        BillingPeriodType? currentBillingPeriod = string.IsNullOrWhiteSpace(billingPeriod)
            ? null
            : Enumeration.GetAll<BillingPeriodType>()
                .FirstOrDefault(period =>
                    string.Equals(period.Name, billingPeriod.Trim(), StringComparison.OrdinalIgnoreCase));

        return currentBillingPeriod is null ? fallbackPriceAmount : (int?)(await priceRepository.GetByTierIdAndBillingPeriodAsync(currentTierId, currentBillingPeriod.Id, cancellationToken))?.Amount ?? fallbackPriceAmount;
    }

    private static string? ResolvePendingTierType(int? pendingTierId) =>
        pendingTierId is { } tierId ? Enumeration.GetAll<UserTierType>().FirstOrDefault(tier => tier.Id == tierId)?.Name : null;
}
