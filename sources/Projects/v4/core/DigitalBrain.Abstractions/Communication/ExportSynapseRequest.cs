using DigitalBrain.Core.Clusters;
using DigitalBrain.Core.Synapses;

namespace DigitalBrain.Abstractions.Communication;

[GenerateSerializer]
public record ExportSynapseRequest(
    [property: Id(0)] Synapse Synapse,
    [property: Id(1)] BrainScope TargetScope) : Synapse;
