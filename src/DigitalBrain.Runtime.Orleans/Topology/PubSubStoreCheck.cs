using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>Fails silo startup when <see cref="TopologyValidator.ValidatePubSubStore"/> reports an issue (RT-06).</summary>
internal sealed class PubSubStoreCheck(Topology topology, IServiceProvider services) : ILifecycleParticipant<ISiloLifecycle>
{
    public void Participate(ISiloLifecycle lifecycle)
        => lifecycle.Subscribe(nameof(PubSubStoreCheck), ServiceLifecycleStage.First, _ =>
        {
            Run();
            return Task.CompletedTask;
        });

    public void Run() => TopologyValidator.ThrowIfInvalid(TopologyValidator.ValidatePubSubStore(topology, services));
}
