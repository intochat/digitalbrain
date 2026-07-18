using Orleans;

namespace DigitalBrain.Runtime.Tasks;

[GenerateSerializer]
public sealed record DurableTaskCompletionSourceState(
    [property: Id(0)] bool IsCompleted,
    [property: Id(1)] bool IsFaulted,
    [property: Id(2)] bool IsCanceled,
    [property: Id(3)] string? Result,
    [property: Id(4)] string? ErrorMessage
);
