namespace DigitalBrain.Behaviors;

using DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.behavior-execution-metadata")]
public sealed record BehaviorExecutionMetadata(
    [property: Id(0)] OwnerId Owner,
    [property: Id(1)] BehaviorId Behavior,
    [property: Id(2)] BehaviorRevisionId Revision,
    [property: Id(3)] BehaviorExecutionId Execution);
