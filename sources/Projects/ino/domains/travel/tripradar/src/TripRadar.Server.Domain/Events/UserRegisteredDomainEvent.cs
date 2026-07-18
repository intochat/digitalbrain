namespace TripRadar.Server.Domain.Events;

public sealed record UserRegisteredDomainEvent(string Email) : IDomainEvent;
