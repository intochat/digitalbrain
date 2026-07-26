namespace DigitalBrain.Behaviors;

using DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.behavior-execution-metadata")]
public sealed record BehaviorExecutionMetadata
{
    public BehaviorExecutionMetadata(
        OwnerId owner,
        BehaviorId behavior,
        BehaviorRevisionId revision,
        BehaviorExecutionId execution)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner.Value);
        behavior.EnsureValid();
        revision.EnsureValid();
        execution.EnsureValid();
        Owner = owner;
        Behavior = behavior;
        Revision = revision;
        Execution = execution;
    }

    [Id(0)]
    public OwnerId Owner { get; }

    [Id(1)]
    public BehaviorId Behavior { get; }

    [Id(2)]
    public BehaviorRevisionId Revision { get; }

    [Id(3)]
    public BehaviorExecutionId Execution { get; }

    public void Deconstruct(
        out OwnerId owner,
        out BehaviorId behavior,
        out BehaviorRevisionId revision,
        out BehaviorExecutionId execution)
    {
        owner = Owner;
        behavior = Behavior;
        revision = Revision;
        execution = Execution;
    }
}
