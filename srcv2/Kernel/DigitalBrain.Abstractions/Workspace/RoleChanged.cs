namespace DigitalBrain.Abstractions.Workspace;

[GenerateSerializer]
[Alias("db.workspace.role-changed")]
public sealed record RoleChanged(
    [property: Id(0)] ActorContext Actor,
    [property: Id(1)] DateTimeOffset At,
    [property: Id(2)] PrincipalId PrincipalId,
    [property: Id(3)] WorkspaceRole PreviousRole,
    [property: Id(4)] WorkspaceRole Role) : Synapse;

