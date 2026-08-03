namespace DigitalBrain.Behaviors;

using DigitalBrain.Behaviors.Manifest;

public sealed record BehaviorCompileResult(
    bool Succeeded,
    ReadOnlyMemory<byte> AssemblyBytes,
    string Diagnostics,
    string CompilerEvidenceJson,
    BehaviorContractManifest? Contract,
    BehaviorCompilerPolicy Policy,
    IReadOnlyList<BehaviorCapabilityGrant> CapabilityGrants,
    IReadOnlyList<string> EventAliases,
    IReadOnlyList<string>? BroadcastEmitAliases);
