using DigitalBrain.Core.Synapses;
using Orleans;

namespace DigitalBrain.Abstractions.Bundles;

// Emitted to the global timeline when the Kernel installs a bundle at boot, so the install path
// is observable on the same tape as every other substrate event.
[GenerateSerializer]
public sealed record BundleInstalled([property: Id(0)] string BundleId) : Synapse;
