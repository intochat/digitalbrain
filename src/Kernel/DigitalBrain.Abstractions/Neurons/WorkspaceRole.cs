namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.workspace-role")]
public enum WorkspaceRole
{
    Owner = 0,
    Admin = 1,
    Builder = 2,
    Viewer = 3,
}
