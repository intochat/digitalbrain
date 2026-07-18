namespace TripRadar.Server.Domain.Events;

public sealed record UserPasswordChangedDomainEvent(long UserId, string Email) : IDomainEvent;
