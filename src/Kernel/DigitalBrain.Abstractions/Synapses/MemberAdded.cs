namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.workspace.member-added")]
public sealed record MemberAdded(
    [property: Id(0)] ActorContext Actor,
    [property: Id(1)] DateTimeOffset At,
    [property: Id(2)] WorkspaceMember Member) : Synapse;

