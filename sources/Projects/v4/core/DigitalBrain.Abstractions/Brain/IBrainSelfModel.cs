using DigitalBrain.Core.Runtime.Reflection;

namespace DigitalBrain.Abstractions.Brain;

// The kernel hosts the DigitalBrain grain but must not reference any bundle (e.g. Ino).
// A bundle that wants the brain to describe itself as that bundle's primary neuron supplies
// this model through DI; without it the brain falls back to a generic substrate description.
public interface IBrainSelfModel
{
    string PersonaName { get; }
    string PersonaDescription { get; }
    Type PrimaryNeuronType { get; }
    IReadOnlyList<string> SelfReferences { get; }
    IReadOnlyList<BrainCapabilityDescriptor> Capabilities { get; }
}
