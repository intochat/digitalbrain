using Orleans.Concurrency;

namespace DigitalBrain.Contracts;

// ABI-bound, see docs/superpowers/specs/2026-10-02-digitalbrain-kernel-split-design.md:
// this is the Orleans binding of the pure model in src/DigitalBrain (there: Watch returns the
// ISynapse and observers receive the pure Signal). Re-deriving from the pure interfaces waits
// for Signal unification — and do NOT add a ProjectReference to src/DigitalBrain before then:
// the pure namespace is DigitalBrain, and enclosing-namespace lookup would silently rebind
// Signal/INeuron in every module type declared under a DigitalBrain.* namespace.
public interface INeuron : IGrainWithStringKey
{
    Task<Guid> Watch(INeuronObserver observer);
    // A turn that stops a subscriber, such as an app uninstalling its script, must not deadlock on
    // that subscriber's final Unwatch. Removing an observer is safe while signals are being delivered.
    [AlwaysInterleave] Task Unwatch(INeuronObserver observer);
}
