namespace DigitalBrain.Poc.Runtime;

public sealed record RouteBinding(
    string OwnerId,
    string ContractAlias,
    CandidateFamilyId? CandidateFamily,
    string? TargetRevision,
    CandidateModuleIdentity? TargetModuleIdentity,
    string? TargetScope,
    string NeuronType)
{
    public string Key => string.Join(
        "|",
        OwnerId,
        ContractAlias,
        CandidateFamily?.Value ?? string.Empty,
        TargetRevision ?? string.Empty,
        TargetModuleIdentity?.AssemblySha256 ?? string.Empty,
        TargetModuleIdentity?.SourceSha256 ?? string.Empty,
        TargetModuleIdentity?.EvidenceSha256 ?? string.Empty,
        TargetScope ?? string.Empty,
        NeuronType);

    public static RouteBinding Candidate(
        string ownerId,
        string contractAlias,
        CandidateFamilyId family,
        string revision,
        CandidateModuleIdentity identity,
        string neuronType) =>
        new(ownerId, contractAlias, family, revision, identity, family.Value, neuronType);
}
