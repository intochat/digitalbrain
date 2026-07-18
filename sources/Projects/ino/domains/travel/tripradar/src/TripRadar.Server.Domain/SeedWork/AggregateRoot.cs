using TripRadar.Server.Domain.Events;

namespace TripRadar.Server.Domain.SeedWork;

public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    public IReadOnlyCollection<IDomainEvent> DequeueDomainEvents()
    {
        if (_domainEvents.Count == 0)
            return [];

        var events = _domainEvents.ToArray();
        _domainEvents.Clear();
        return events;
    }
}
