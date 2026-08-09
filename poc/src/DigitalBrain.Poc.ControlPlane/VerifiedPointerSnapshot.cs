namespace DigitalBrain.Poc.ControlPlane;

public sealed record VerifiedPointerSnapshot(
    CandidatePointerHead Head,
    ActiveCandidatePointer? Pointer)
{
    public bool IsEmpty => Pointer is null;
}
