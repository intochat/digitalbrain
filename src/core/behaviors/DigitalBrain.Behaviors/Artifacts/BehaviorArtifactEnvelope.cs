namespace DigitalBrain.Behaviors.Artifacts;

using DigitalBrain.Behaviors.Manifest;

public sealed record BehaviorArtifactEnvelope(
    BehaviorDefinitionManifest Manifest,
    string ProgramSource,
    string PackageLockJson,
    ReadOnlyMemory<byte> BehaviorAssembly,
    string BehaviorDependenciesJson,
    IReadOnlyDictionary<string, string> Features,
    string CompilerEvidenceJson,
    string AdmissionEvidenceJson,
    string BddEvidenceJson);
