namespace TripRadar.Server.Domain.Events;

public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    IReadOnlyCollection<IDomainEvent> DequeueDomainEvents();
}
