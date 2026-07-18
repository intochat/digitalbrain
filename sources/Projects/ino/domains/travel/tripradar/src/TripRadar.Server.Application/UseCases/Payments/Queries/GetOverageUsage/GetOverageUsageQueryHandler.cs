using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Policies;

namespace TripRadar.Server.Application.UseCases.Payments.Queries.GetOverageUsage;

public class GetOverageUsageQueryHandler(
    IUserMonthlyTokenCountRepository userMonthlyTokenCountRepository,
    IUserSubscriptionRepository userSubscriptionRepository,
    IOverageBillingRecordRepository overageBillingRecordRepository,
    ICurrentUserContext currentUserContext)
    : IRequestHandler<GetOverageUsageQuery, Result<GetOverageUsageDTO>>
{
    public async Task<Result<GetOverageUsageDTO>> Handle(GetOverageUsageQuery request,
        CancellationToken cancellationToken)
    {
        var user = currentUserContext.User;
        if (user is null)
        {
            return Result.Failure<GetOverageUsageDTO>(Errors.UserNotFound);
        }

        var currentDate = DateTime.UtcNow;

        var monthlyUsage = await userMonthlyTokenCountRepository.GetByUserIdAsync(user.Id, cancellationToken);
        var overageRecords = await overageBillingRecordRepository.GetByUserIdAndMonthAsync(user.Id, currentDate.Year, currentDate.Month, cancellationToken);
        var userSubscription = await userSubscriptionRepository.GetByUserIdAsync(user.Id, cancellationToken);

        var totalOverageTokens = monthlyUsage?.OverageTokensConsumed ?? 0;
        var totalOverageCharges = overageRecords.Sum(record => record.TotalCharge);
        var isEligible = PaidTierEligibilityPolicy.IsPaidTier(user);
        var payAsYouGoEnabled = userSubscription?.PayAsYouGoEnabled ?? false;
        var currency = overageRecords
            .LastOrDefault(record => record.Currency is not null)?
            .Currency?
            .CurrencyCode
            ?? "USD";

        return Result.Success(new GetOverageUsageDTO(user.Profile.Username ?? string.Empty, user.Tier.Name, monthlyUsage?.TokensConsumed ?? 0,
            totalOverageTokens, totalOverageCharges, currency, currentDate.Year, currentDate.Month, isEligible, payAsYouGoEnabled));
    }
}



