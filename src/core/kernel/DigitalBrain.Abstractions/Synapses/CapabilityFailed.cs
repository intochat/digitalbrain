namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.capability-failed")]
public sealed record CapabilityFailed([property: Id(0)] SynapseId Request) : Synapse;
