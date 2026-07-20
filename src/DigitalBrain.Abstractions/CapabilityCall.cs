namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.capability-call")]
public sealed record CapabilityCall(
    [property: Id(0)] string Interface,
    [property: Id(1)] string Method,
    [property: Id(2)] string Target) : Synapse;
