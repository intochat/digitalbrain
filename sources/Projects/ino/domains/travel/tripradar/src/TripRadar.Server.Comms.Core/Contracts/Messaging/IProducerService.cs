using TripRadar.Server.Comms.Core.Events;

namespace TripRadar.Server.Comms.Core.Contracts.Messaging;

public interface IProducerService
{
    Task ProduceAsync<T>(T @event, CancellationToken cancellationToken = default) where T : PublishableEvent;
}
