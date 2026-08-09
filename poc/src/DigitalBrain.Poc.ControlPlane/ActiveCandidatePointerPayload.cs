namespace DigitalBrain.Poc.ControlPlane;

internal sealed record ActiveCandidatePointerPayload(
    string OwnerId,
    string FamilyId,
    string CandidateSourceHash,
    string PreviousCandidateSourceHash,
    string ParentPayloadHash,
    long Version);
