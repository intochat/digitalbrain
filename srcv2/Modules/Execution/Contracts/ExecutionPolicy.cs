using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.policy")]
public sealed record ExecutionPolicy(
    [property: Id(0)] int MaximumAttempts,
    [property: Id(1)] TimeSpan RetryDelay,
    [property: Id(2)] DateTimeOffset? Deadline);

