namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.capability-rejected")]
public sealed record CapabilityRejected([property: Id(0)] SynapseId Request) : Synapse;
