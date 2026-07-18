using Microsoft.Extensions.DependencyInjection;
using TripRadar.Server.Domain.Events;

namespace TripRadar.Server.Infrastructure.Services;

public class DomainEventDispatcher(IServiceProvider serviceProvider) : IDomainEventDispatcher
{
    public void Publish(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        Publish((dynamic)domainEvent);
    }

    public void Publish<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent
    {
        var handlers = serviceProvider.GetServices<IDomainEventHandler<TEvent>>();

        foreach (var handler in handlers)
        {
            handler.Handle(domainEvent);
        }
    }
}
