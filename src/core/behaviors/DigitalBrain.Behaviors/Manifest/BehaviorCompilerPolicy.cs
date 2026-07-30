namespace DigitalBrain.Behaviors.Manifest;

public sealed record BehaviorCompilerPolicy(
    string SdkVersion,
    string RoslynVersion,
    string LanguageVersion,
    string PolicyId);
