using DigitalBrain.Protocol.Domain.Events;
using Orleans.Concurrency;

namespace DigitalBrain.Protocol;

public interface INeuron : IGrainWithStringKey
{
    [AlwaysInterleave]
    Task DeliverAsync(Synapse synapse);
    Task EnsureActiveAsync();
}

public interface IHandle<in TSynapse> where TSynapse : Synapse
{
    Task HandleAsync(TSynapse synapse, CancellationToken cancellationToken);
}

public interface IEmit<TSynapse> where TSynapse : Synapse;
