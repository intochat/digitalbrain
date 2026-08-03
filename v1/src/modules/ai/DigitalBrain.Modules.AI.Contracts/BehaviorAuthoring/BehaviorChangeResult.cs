namespace DigitalBrain.AI;

public sealed record BehaviorChangeResult(
    string ProgramSource,
    string FeatureText,
    string FeatureName,
    bool ReadyForPropose);
