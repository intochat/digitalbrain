using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Generated;

[ExcludeFromCodeCoverage]
internal static class DispatchManifest
{
    internal static readonly (string Neuron, string Synapse, bool IsHandler)[] Wirings =
    [
        ("DigitalBrain.Memory.ProjectionBootNeuron", "DigitalBrain.Abstractions.DigitalBrainActivated", true),
        ("DigitalBrain.Memory.ProjectionBootNeuron", "DigitalBrain.Memory.VectorProjectionReconciled", false),
        ("DigitalBrain.Memory.VectorMemoryNeuron", "DigitalBrain.Memory.RemoveVectorMemory", true),
        ("DigitalBrain.Memory.VectorMemoryNeuron", "DigitalBrain.Memory.SearchVectorMemory", true),
        ("DigitalBrain.Memory.VectorMemoryNeuron", "DigitalBrain.Memory.StoreVectorMemory", true),
        ("DigitalBrain.Memory.VectorMemoryNeuron", "DigitalBrain.Memory.VectorMemoryMatches", false),
        ("DigitalBrain.Memory.VectorMemoryNeuron", "DigitalBrain.Memory.VectorMemoryRemoved", false),
        ("DigitalBrain.Memory.VectorMemoryNeuron", "DigitalBrain.Memory.VectorMemoryStored", false),
    ];
}
