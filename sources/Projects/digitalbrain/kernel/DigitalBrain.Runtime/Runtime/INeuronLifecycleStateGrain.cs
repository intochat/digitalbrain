namespace DigitalBrain.Runtime.Runtime;

// E-RUN #38. Per-neuron persistent state for lifecycle dispatch — currently
// just the "has this neuron ever been activated" bit, which the runtime needs
// to distinguish first-ever activation (fires `created`) from a steady
// re-activation (fires only `activated`).
//
// Kept as a sibling grain rather than upgrading IInterpretedNeuronGrain to a
// DurableGrain so the interpreted grain's surface stays narrow — most tests
// for the interpreted grain don't care about lifecycle and would otherwise
// have to wire IStateMachineStorageProvider + AddStateMachineStorage. This
// mirrors BrainCatalogGrain / SynapseLogGrain: durability lands on a grain
// only when remembering is its actual job.
//
// Primary key matches the neuron FQN, the same key the interpreted grain uses
// (IGrainWithStringKey ⇒ string-keyed). One state grain per authored neuron.
public interface INeuronLifecycleStateGrain : IGrainWithStringKey
{
    // Returns true iff this is the first activation in the cluster's history
    // for this neuron FQN. After the first call returns true, subsequent calls
    // return false. The write happens before the return so observers cannot
    // see a "first-activation" flap on a crash/replay.
    Task<bool> MarkActivatedAsync();
}
