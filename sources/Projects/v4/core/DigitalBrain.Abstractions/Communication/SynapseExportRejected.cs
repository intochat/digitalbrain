using DigitalBrain.Core.Clusters;
using DigitalBrain.Core.Synapses;

namespace DigitalBrain.Abstractions.Communication;

[GenerateSerializer]
public record SynapseExportRejected(
    [property: Id(0)] Guid SourceSynapseId,
    [property: Id(1)] SynapseExportRejectionReason Reason) : Synapse;
