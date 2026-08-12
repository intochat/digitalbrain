using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.dependency-pending")]
public sealed record DependencyPending(BlockerId Id) : ExecutionBlocker(Id);

