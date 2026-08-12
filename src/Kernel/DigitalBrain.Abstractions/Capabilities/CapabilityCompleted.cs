namespace DigitalBrain.Abstractions.Capabilities;

[GenerateSerializer]
[Alias("db.capability-completed")]
public sealed record CapabilityCompleted([property: Id(0)] SynapseId Request) : Synapse;
