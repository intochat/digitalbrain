namespace DigitalBrain.Kernel.Introspector;

// Orleans encodes [GenerateSerializer] types by full name unless aliased; a
// stable [Alias] keeps this record resilient to namespace/type moves (Orleans
// best practice). State storage is volatile today, so this pins future wire
// identity, not a migration of existing data.
[Alias("DigitalBrain.OutstandingExplain")]
[GenerateSerializer]
public sealed record OutstandingExplain(
    [property: Id(0)] Guid    CorrelationId,
    [property: Id(1)] Guid    OriginalCallerNeuronId,
    [property: Id(2)] string? OriginalCallerNeuronType,
    [property: Id(3)] Guid    OriginalRequestSynapseId);
