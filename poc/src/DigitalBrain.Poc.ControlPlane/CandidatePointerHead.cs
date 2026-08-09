namespace DigitalBrain.Poc.ControlPlane;

public sealed record CandidatePointerHead(
    string OwnerId,
    string FamilyId,
    string CurrentPayloadHash,
    string ParentPayloadHash,
    string CurrentCandidateSourceHash,
    string PreviousCandidateSourceHash,
    long Version)
{
    public static CandidatePointerHead Empty(string ownerId, string familyId)
    {
        var zero = new string('0', 64);
        return new CandidatePointerHead(ownerId, familyId, zero, zero, zero, zero, 0);
    }

    public static CandidatePointerHead From(ActiveCandidatePointer pointer) =>
        new(
            pointer.OwnerId,
            pointer.FamilyId,
            pointer.PayloadHash,
            pointer.ParentPayloadHash,
            pointer.CandidateSourceHash,
            pointer.PreviousCandidateSourceHash,
            pointer.Version);
}
