using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.apply-command")]
public abstract record ExecutionApplyCommand;

