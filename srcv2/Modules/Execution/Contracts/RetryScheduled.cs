using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.retry-scheduled")]
public sealed record RetryScheduled(BlockerId Id) : ExecutionBlocker(Id);

