namespace DigitalBrain.Abstractions.Capabilities;

[GenerateSerializer]
[Alias("db.capability-requested")]
public sealed record CapabilityRequested(
    [property: Id(0)] string Contract,
    [property: Id(1)] string Method,
    [property: Id(2)] NeuronId Target) : Synapse;
