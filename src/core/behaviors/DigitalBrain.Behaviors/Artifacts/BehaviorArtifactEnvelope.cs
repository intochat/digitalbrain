namespace DigitalBrain.Behaviors.Artifacts;

using DigitalBrain.Behaviors.Manifest;

public sealed record BehaviorArtifactEnvelope(
    BehaviorDefinitionManifest Manifest,
    string ProgramSource,
    string FeatureSource,
    string PackageLockJson,
    ReadOnlyMemory<byte> BehaviorAssembly,
    string BehaviorDependenciesJson,
    string CompilerEvidenceJson,
    string AdmissionEvidenceJson,
    string BddEvidenceJson);
