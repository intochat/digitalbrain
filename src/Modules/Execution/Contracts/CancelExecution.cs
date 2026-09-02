using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.cancel.v1")]
public sealed record CancelExecution(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] ExecutionId ExecutionId) : Signal;
