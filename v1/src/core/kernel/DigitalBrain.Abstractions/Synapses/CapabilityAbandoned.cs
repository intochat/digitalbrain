namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.capability-abandoned")]
public sealed record CapabilityAbandoned([property: Id(0)] SynapseId Request) : Synapse;
