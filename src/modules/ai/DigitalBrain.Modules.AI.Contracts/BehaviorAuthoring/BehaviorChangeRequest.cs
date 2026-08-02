namespace DigitalBrain.AI;

public sealed record BehaviorChangeRequest(
    string BehaviorId,
    string RequestText,
    string CurrentFeatureText,
    string CurrentProgramSource,
    string DisplayName,
    string FeatureName);
