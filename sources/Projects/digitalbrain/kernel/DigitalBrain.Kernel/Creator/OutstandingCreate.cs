namespace DigitalBrain.Kernel.Creator;

[GenerateSerializer]
public sealed record OutstandingCreate(
    [property: Id(0)] Guid    CorrelationId,
    [property: Id(1)] string  Intent,
    [property: Id(2)] string  SuggestedNeuronId,
    [property: Id(3)] Guid    OriginalRequestSynapseId,
    [property: Id(4)] Guid    OriginalCallerNeuronId,
    [property: Id(5)] string? OriginalCallerNeuronType,
    [property: Id(6)] int     Attempt,
    [property: Id(7)] string? LastError,
    [property: Id(8)] string? PinnedLlmModel = null);
