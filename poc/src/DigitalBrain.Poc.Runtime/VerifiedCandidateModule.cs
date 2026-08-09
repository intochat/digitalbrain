namespace DigitalBrain.Poc.Runtime;

internal sealed record VerifiedCandidateModule(
    string OwnerId,
    CandidateFamilyId Family,
    string Revision,
    string AssemblyPath,
    string EvidencePath,
    string AssemblySha256,
    IReadOnlyList<string> GrantedInputAliases,
    IReadOnlyList<string> GrantedOutputAliases,
    IReadOnlyList<string> GrantedTrustedOutputAliases,
    IReadOnlyList<string> GrantedTargetScopes);
