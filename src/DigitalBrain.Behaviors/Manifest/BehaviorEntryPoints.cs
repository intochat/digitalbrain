namespace DigitalBrain.Behaviors.Manifest;

public sealed record BehaviorEntryPoints(
    IReadOnlyList<string> EventAliases,
    IReadOnlyList<string> IntentSchemas);
