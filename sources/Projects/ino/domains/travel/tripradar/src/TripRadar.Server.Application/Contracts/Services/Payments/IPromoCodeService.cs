using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.Contracts.Services.Payments;

public interface IPromoCodeService
{
    /// <summary>
    /// Validates a promo code for a user and creates a corresponding Stripe coupon.
    /// </summary>
    /// <param name="promoCode">The promo code string to validate.</param>
    /// <param name="user">The user applying the promo code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing the validated promo code entity and the Stripe coupon ID, or an error.</returns>
    Task<Result<(PromoCode PromoCode, string StripeCouponId)>> ValidateAndCreateStripeCouponAsync(string promoCode, User user, CancellationToken cancellationToken = default);
}
