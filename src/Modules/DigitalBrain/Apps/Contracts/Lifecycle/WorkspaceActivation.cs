namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("brain.workspace-activation")]
public sealed record WorkspaceActivation([property: Id(0)] long Generation = 0, [property: Id(1)] bool Active = false);
