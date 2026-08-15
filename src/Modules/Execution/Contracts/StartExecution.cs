using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.start")]
public sealed record StartExecution(
    [property: Id(0)] Goal Goal,
    [property: Id(1)] NeuronId Worker,
    [property: Id(2)] ExecutionPolicy Policy,
    [property: Id(3)] NeuronId? RetryOf = null,
    [property: Id(4)] NeuronId? Origin = null) : ExecutionApplyCommand;

