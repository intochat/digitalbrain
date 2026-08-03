using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors;

[GenerateSerializer]
[Alias("db.behavior.task-result")]
public sealed record BehaviorTaskResult(
    [property: Id(0)] string Outcome) : Result;

[GenerateSerializer]
[Alias("db.behavior.task-failure")]
public sealed record BehaviorTaskFailure(
    [property: Id(0)] string Reason) : Failure;
