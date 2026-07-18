namespace TripRadar.Server.Domain.Events;

public sealed record PromoCodeAppliedDomainEvent(long PromoCodeId, string Code, long UserId, decimal DiscountAmount) : IDomainEvent;
