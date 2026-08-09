namespace DigitalBrain.Poc.Host;

public sealed record HostBootResult(
    bool Succeeded,
    BootFailure Failure,
    int ProcessId,
    string ActiveSourceHash,
    HostAttachment? Attachment);
