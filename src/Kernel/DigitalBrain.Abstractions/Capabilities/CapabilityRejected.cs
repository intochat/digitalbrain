namespace DigitalBrain.Abstractions.Capabilities;

[GenerateSerializer]
[Alias("db.capability-rejected")]
public sealed record CapabilityRejected([property: Id(0)] SynapseId Request) : Synapse;
