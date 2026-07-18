using Ino.Core;

namespace Ino.Kernel.Contracts;

/// <summary>
/// Broadcast fired by CortexNeuron (and CortexCapability) when no keyword route matches the
/// inbound text (or when the matched synapse type isn't backed by an installed
/// neuron). Intentionally a broadcast, not a canonical request — reactive
/// listeners in later slices feed marketplace analytics / missed-intent surfaces
/// (plan slice 13: inspector Actions panel; post-v0.1: cross-user aggregation).
///
/// Declared in Ino.Kernel.Contracts so CortexCapability (in Ino.Core.Hosting) can
/// fire it without a circular project reference back to Ino.Kernel.
/// </summary>
[GenerateSerializer]
public sealed record UnroutedIntent(
    [property: Id(0)] string Text,
    [property: Id(1)] string UserId) : ISynapse;
