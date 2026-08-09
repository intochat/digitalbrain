namespace DigitalBrain.Poc.ControlPlane;

public sealed record PointerVerificationResult(
    bool Succeeded,
    PointerVerificationFailure Failure,
    ActiveCandidatePointer? Pointer);
