namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.workspace.change-role")]
public sealed record ChangeRole(
    [property: Id(0)] ActorContext Actor,
    [property: Id(1)] PrincipalId PrincipalId,
    [property: Id(2)] WorkspaceRole Role) : RequestSynapse<RoleChanged>;

