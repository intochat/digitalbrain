namespace DigitalBrain.Abstractions.Workspace;

[GenerateSerializer]
[Alias("db.workspace.remove-member")]
public sealed record RemoveMember(
    [property: Id(0)] ActorContext Actor,
    [property: Id(1)] PrincipalId PrincipalId) : RequestSynapse<MemberRemoved>;

