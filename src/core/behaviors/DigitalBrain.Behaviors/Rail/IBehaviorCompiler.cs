namespace DigitalBrain.Behaviors;

using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Manifest;

public interface IBehaviorCompiler
{
    BehaviorCompileResult Compile(string programSource, BehaviorId behavior);
}

public sealed record BehaviorCompileResult(
    bool Succeeded,
    ReadOnlyMemory<byte> AssemblyBytes,
    string Diagnostics,
    string CompilerEvidenceJson,
    BehaviorContractManifest? Contract,
    BehaviorCompilerPolicy Policy,
    IReadOnlyList<BehaviorCapabilityGrant> CapabilityGrants);
