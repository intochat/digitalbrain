namespace DigitalBrain.Poc.ControlPlane;

public enum PointerVerificationFailure
{
    None,
    Missing,
    Malformed,
    InvalidSignature,
    StaleOrReplayed,
}
