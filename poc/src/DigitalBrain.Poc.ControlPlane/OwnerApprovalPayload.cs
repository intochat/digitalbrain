namespace DigitalBrain.Poc.ControlPlane;

public sealed record OwnerApprovalPayload(
    string CandidateId,
    string RunId,
    string OwnerId,
    string FamilyId,
    string SourceHash,
    string AssemblyHash,
    string CandidateMetadataHash,
    string AttestationPayloadHash);
