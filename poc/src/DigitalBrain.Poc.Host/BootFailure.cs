namespace DigitalBrain.Poc.Host;

public enum BootFailure
{
    None,
    NoActivePointer,
    InvalidPointerSignature,
    StaleOrReplayedPointer,
    CandidateVerificationFailed,
    HostAuthorityUnavailable,
}
