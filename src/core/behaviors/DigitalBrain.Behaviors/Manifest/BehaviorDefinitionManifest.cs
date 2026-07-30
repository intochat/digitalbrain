namespace DigitalBrain.Behaviors.Manifest;

using DigitalBrain.Abstractions;

public sealed record BehaviorDefinitionManifest(
    BehaviorId Behavior,
    string DisplayName,
    string Description,
    BehaviorEntryPoints EntryPoints,
    IReadOnlyList<BehaviorScenarioManifest> Scenarios,
    string Overview,
    BehaviorCompilerPolicy CompilerPolicy,
    IReadOnlyList<BehaviorCapabilityGrant> CapabilityGrants,
    BehaviorResourceLimits ResourceLimits);
