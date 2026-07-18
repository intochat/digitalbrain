namespace TripRadar.Server.Domain.Rules;

public static class DomainErrors
{
    public static readonly DomainError PromoCodeExpired = new("PROMO_CODE_EXPIRED", "The promo code has expired.");

    public static readonly DomainError PromoCodeInactive = new("PROMO_CODE_INACTIVE", "The promo code is not active.");

    public static readonly DomainError PromoCodeUsageLimitExceeded = new("PROMO_CODE_USAGE_LIMIT_EXCEEDED", "The promo code usage limit has been reached.");

    public static readonly DomainError PromoCodeAlreadyUsedByUser = new("PROMO_CODE_ALREADY_USED_BY_USER", "User has already used this promo code.");

    public static readonly DomainError PromoCodeNotStarted = new("PROMO_CODE_NOT_STARTED", "The promo code is not valid yet.");
}
