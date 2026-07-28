namespace DigitalBrain.Behaviors;

using DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.behavior-execution-metadata")]
public sealed record BehaviorExecutionMetadata
{
    public BehaviorExecutionMetadata(OwnerId Owner, BehaviorId Behavior, BehaviorRevisionId Revision, BehaviorExecutionId Execution)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Owner.Value);
        Behavior.EnsureValid();
        Revision.EnsureValid();
        Execution.EnsureValid();
        this.Owner = Owner;
        this.Behavior = Behavior;
        this.Revision = Revision;
        this.Execution = Execution;
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
