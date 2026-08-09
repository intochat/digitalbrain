namespace DigitalBrain.Poc.ControlPlane;

public sealed record ActiveCandidatePointer(
    string OwnerId,
    string FamilyId,
    string CandidateSourceHash,
    string PreviousCandidateSourceHash,
    string ParentPayloadHash,
    long Version,
    string PayloadHash,
    string Algorithm,
    string PublicKey,
    string Signature)
{
    public static ActiveCandidatePointer Next(
        CandidatePointerHead head,
        string candidateSourceHash) =>
        new(
            head.OwnerId,
            head.FamilyId,
            candidateSourceHash.ToLowerInvariant(),
            head.CurrentCandidateSourceHash,
            head.CurrentPayloadHash,
            checked(head.Version + 1),
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);

    public static ActiveCandidatePointer Rollback(CandidatePointerHead head) =>
        new(
            head.OwnerId,
            head.FamilyId,
            head.PreviousCandidateSourceHash,
            head.CurrentCandidateSourceHash,
            head.CurrentPayloadHash,
            checked(head.Version + 1),
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);

    public static ActiveCandidatePointer EmptyRecovery(CandidatePointerHead head) =>
        new(
            head.OwnerId,
            head.FamilyId,
            new string('0', 64),
            head.CurrentCandidateSourceHash,
            head.CurrentPayloadHash,
            checked(head.Version + 1),
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);
}
