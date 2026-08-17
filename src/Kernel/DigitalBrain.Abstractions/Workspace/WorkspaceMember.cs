namespace DigitalBrain.Abstractions.Workspace;

[GenerateSerializer]
[Alias("db.workspace-member")]
public sealed record WorkspaceMember(
    [property: Id(0)] PrincipalId PrincipalId,
    [property: Id(1)] string Username,
    [property: Id(2)] WorkspaceRole Role);
