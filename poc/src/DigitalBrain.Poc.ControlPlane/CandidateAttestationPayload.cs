using System.Collections.Generic;

namespace DigitalBrain.Poc.ControlPlane;

public sealed record CandidateAttestationPayload(
    string CandidateId,
    string RunId,
    string OwnerId,
    string FamilyId,
    string SourceHash,
    string AssemblyHash,
    string CandidateMetadataHash,
    string ScenarioHash)
{
    public string Revision { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string SourcePath { get; init; } = string.Empty;

    public string AssemblyPath { get; init; } = string.Empty;

    public IReadOnlyList<string> GrantedInputAliases { get; init; } = [];

    public IReadOnlyList<string> GrantedCandidateOutputAliases { get; init; } = [];

    public IReadOnlyList<string> GrantedTrustedOutputAliases { get; init; } = [];

    public IReadOnlyList<string> GrantedTargetScopes { get; init; } = [];

    public IReadOnlyList<string> ResolvedReferences { get; init; } = [];

    public string NormalizedAstHash { get; init; } = string.Empty;

    public string FixedHeaderHash { get; init; } = string.Empty;

    public string CompilerHash { get; init; } = string.Empty;

    public string SdkHash { get; init; } = string.Empty;

    public string ReferencesHash { get; init; } = string.Empty;

    public string CapabilitiesHash { get; init; } = string.Empty;

    public string ContractsHash { get; init; } = string.Empty;

    public string StateSchemaHash { get; init; } = string.Empty;
}
