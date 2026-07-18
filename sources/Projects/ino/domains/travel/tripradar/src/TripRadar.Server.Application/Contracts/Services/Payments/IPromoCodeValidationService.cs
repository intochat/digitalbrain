using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.Contracts.Services.Payments;

public interface IPromoCodeValidationService
{
    /// <summary>
    /// Validates if a promo code can be used by a specific user
    /// </summary>
    /// <param name="promoCode">The promo code to validate</param>
    /// <param name="userId">The user ID attempting to use the promo code</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success result if valid, failure result with specific error if invalid</returns>
    Task<Result> ValidatePromoCodeForUserAsync(PromoCode promoCode, long userId, CancellationToken cancellationToken = default);
}
