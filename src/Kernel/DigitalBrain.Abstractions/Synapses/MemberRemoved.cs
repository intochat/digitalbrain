namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.workspace.member-removed")]
public sealed record MemberRemoved(
    [property: Id(0)] ActorContext Actor,
    [property: Id(1)] DateTimeOffset At,
    [property: Id(2)] WorkspaceMember Member) : Synapse;

