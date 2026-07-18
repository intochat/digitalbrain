using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

/// <summary>
/// Synapse request to orchestrate directory creation via NavigatorNeuron.
/// </summary>
[GenerateSerializer]
public sealed record CreateFolderRequest(
    [property: Id(1)] string Prompt
) : Synapse;
