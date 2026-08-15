using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.input-required")]
public sealed record InputRequired(BlockerId Id) : ExecutionBlocker(Id);

