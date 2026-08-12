namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.workspace.add-member")]
public sealed record AddMember(
    [property: Id(0)] ActorContext Actor,
    [property: Id(1)] PrincipalId PrincipalId,
    [property: Id(2)] string Username,
    [property: Id(3)] WorkspaceRole Role) : RequestSynapse<MemberAdded>;

