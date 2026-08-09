namespace DigitalBrain.Poc.Runtime;

public sealed record CandidateCatalogRecord(
    string CandidateId,
    string RunId,
    string OwnerId,
    CandidateFamilyId Family,
    string Revision,
    string SourceHash,
    string AssemblyHash,
    string CandidateMetadataHash,
    string StateSchemaHash);
