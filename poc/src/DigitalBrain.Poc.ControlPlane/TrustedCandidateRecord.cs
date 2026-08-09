using System.Collections.Generic;

namespace DigitalBrain.Poc.ControlPlane;

public sealed record TrustedCandidateRecord(
    string CandidateId,
    string RunId,
    string OwnerId,
    string FamilyId,
    string Revision,
    string Status,
    string SourcePath,
    string AssemblyPath,
    string SourceHash,
    string AssemblyHash,
    string CandidateMetadataHash,
    IReadOnlyList<string> GrantedInputAliases,
    IReadOnlyList<string> GrantedCandidateOutputAliases,
    IReadOnlyList<string> GrantedTrustedOutputAliases,
    IReadOnlyList<string> GrantedTargetScopes,
    string StateSchemaHash,
    IReadOnlyList<string> ResolvedReferences);
