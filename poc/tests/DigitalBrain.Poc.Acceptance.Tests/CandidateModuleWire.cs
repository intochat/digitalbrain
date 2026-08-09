namespace DigitalBrain.Poc.Acceptance.Tests;

internal sealed record CandidateModuleWire(
    string OwnerId,
    string Family,
    string Revision,
    string AssemblyPath,
    string EvidencePath,
    string AssemblySha256,
    string[] GrantedInputAliases,
    string[] GrantedOutputAliases,
    string[] GrantedTrustedOutputAliases,
    string[] GrantedTargetScopes);
