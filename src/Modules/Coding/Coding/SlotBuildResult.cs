namespace DigitalBrain.Coding;

public sealed record SlotBuildResult(BuildOutcome Build, string ArtifactsPath, bool TouchesSerializedState);
