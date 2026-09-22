using DigitalBrain.Contracts;

namespace DigitalBrain.AI;

[GenerateSerializer, Alias("ai.inference-started")]
public sealed record InferenceStarted([property: Id(0)] string NeuronId, [property: Id(1)] string OperationId) : Signal;
[GenerateSerializer, Alias("ai.inference-completed")]
public sealed record InferenceCompleted([property: Id(0)] string NeuronId, [property: Id(1)] string OperationId) : Signal;
[GenerateSerializer, Alias("ai.inference-failed")]
public sealed record InferenceFailed([property: Id(0)] string NeuronId, [property: Id(1)] string OperationId,
    [property: Id(2)] string Reason) : Signal;