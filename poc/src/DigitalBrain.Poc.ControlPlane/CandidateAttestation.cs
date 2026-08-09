namespace DigitalBrain.Poc.ControlPlane;

public sealed record CandidateAttestation(
    CandidateAttestationPayload Payload,
    string Algorithm,
    string PublicKey,
    string Signature);
