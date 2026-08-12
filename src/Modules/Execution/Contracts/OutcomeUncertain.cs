using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.outcome-uncertain")]
public sealed record OutcomeUncertain(BlockerId Id) : ExecutionBlocker(Id);

