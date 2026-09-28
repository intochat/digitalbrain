namespace DigitalBrain.Microsoft.Roslyn;

[GenerateSerializer]
[Alias("coding.workspace-phase")]
public enum WorkspacePhase
{
    NotOpened = 0,
    Opening = 1,
    Ready = 2,
    Failed = 3,
}