using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Core;

// Test seam called after the snapshot write commits.
// Nothing registers an implementation outside tests.
internal interface IReactionCrashPoint
{
    void AfterSnapshotSave(NeuronId neuron);
}
