namespace DigitalBrain.Poc.ControlPlane;

public sealed record AttestationVerificationResult(
    bool Succeeded,
    AttestationFailure Failure);
