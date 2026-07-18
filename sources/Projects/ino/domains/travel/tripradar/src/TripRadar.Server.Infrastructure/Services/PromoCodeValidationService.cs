using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Services;

public class PromoCodeValidationService(IPromoCodeUsageRepository promoCodeUsageRepository) : IPromoCodeValidationService
{
    public async Task<Result> ValidatePromoCodeForUserAsync(PromoCode promoCode, long userId, CancellationToken cancellationToken = default)
    {
        var basicValidation = ValidatePromoCodeBasicProperties(promoCode);
        if (basicValidation.IsFailure)
            return basicValidation;

        var userUsageCount = await promoCodeUsageRepository.GetUsageCountByUserAsync(promoCode.Id, userId, cancellationToken);
        return userUsageCount >= promoCode.MaxUsagePerUser ? Result.Failure(Errors.PromoCodeAlreadyUsedByUser) : Result.Success();
    }

    private static Result ValidatePromoCodeBasicProperties(PromoCode promoCode) =>
        !promoCode.IsActive ? Result.Failure(Errors.PromoCodeInactive) :
        promoCode.IsNotStarted() ? Result.Failure(Errors.PromoCodeNotStarted) :
        promoCode.IsExpired() ? Result.Failure(Errors.PromoCodeExpired) :
        promoCode.HasReachedMaxUsage() ? Result.Failure(Errors.PromoCodeUsageLimitExceeded) : Result.Success();
}
