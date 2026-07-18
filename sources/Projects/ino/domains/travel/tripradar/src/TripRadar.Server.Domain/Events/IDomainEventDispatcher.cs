namespace TripRadar.Server.Domain.Events;

public interface IDomainEventDispatcher
{
    void Publish(IDomainEvent domainEvent);
}
