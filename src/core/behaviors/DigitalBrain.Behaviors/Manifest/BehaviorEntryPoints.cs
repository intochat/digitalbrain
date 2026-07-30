namespace DigitalBrain.Behaviors.Manifest;

public sealed record BehaviorEntryPoints(
    IReadOnlyList<string> EventAliases,
    BehaviorContractManifest Contract);
