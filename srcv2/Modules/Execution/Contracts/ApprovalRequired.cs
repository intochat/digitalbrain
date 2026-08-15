using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.approval-required")]
public sealed record ApprovalRequired(BlockerId Id) : ExecutionBlocker(Id);

