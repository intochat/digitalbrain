namespace DigitalBrain.Poc.ControlPlane;

public sealed record OwnerApproval(
    OwnerApprovalPayload Payload,
    string Algorithm,
    string PublicKey,
    string Signature);
