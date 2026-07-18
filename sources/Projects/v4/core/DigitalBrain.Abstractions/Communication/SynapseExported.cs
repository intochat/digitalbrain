using DigitalBrain.Core.Synapses;

namespace DigitalBrain.Abstractions.Communication;

[GenerateSerializer]
public record SynapseExported(
    [property: Id(0)] Guid SourceSynapseId,
    [property: Id(1)] Synapse ExportedSynapse) : Synapse;
