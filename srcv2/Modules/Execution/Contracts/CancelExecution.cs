using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.cancel")]
public sealed record CancelExecution : ExecutionApplyCommand;

