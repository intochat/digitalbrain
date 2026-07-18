using DigitalBrain.Runtime.Runtime;
using Orleans.Journaling;

namespace DigitalBrain.Kernel.Runtime;

// E-RUN #38. Durable per-neuron flag — has this FQN ever been activated in
// this cluster? IDurableValue<bool> backed by AddStateMachineStorage (the same
// machinery BrainCatalogGrain and SynapseLogGrain use), so the bit survives
// silo restarts and replay; an activated/created split that flapped on every
// silo cycle would defeat the "first-ever" semantics the lifecycle engine
// gives an authored neuron.
internal sealed class NeuronLifecycleStateGrain(
    [FromKeyedServices("hasBeenActivated")] IDurableValue<bool> hasBeenActivated)
    : DurableGrain, INeuronLifecycleStateGrain
{
    public async Task<bool> MarkActivatedAsync()
    {
        if (hasBeenActivated.Value)
            return false;

        hasBeenActivated.Value = true;
        await WriteStateAsync();
        return true;
    }
}
