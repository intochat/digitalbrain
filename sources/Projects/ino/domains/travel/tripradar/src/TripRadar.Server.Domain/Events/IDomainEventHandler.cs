namespace TripRadar.Server.Domain.Events;

/// <summary>
/// Interface for handling domain events.
/// </summary>
/// <typeparam name="TEvent">The type of domain event to handle.</typeparam>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    /// <summary>
    /// Handles the domain event asynchronously.
    /// </summary>
    void Handle(TEvent domainEvent);
}
