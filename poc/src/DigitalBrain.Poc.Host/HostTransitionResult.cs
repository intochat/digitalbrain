namespace DigitalBrain.Poc.Host;

public sealed record HostTransitionResult(
    bool Succeeded,
    PromotionFailure Failure,
    int ProcessId,
    string ActiveSourceHash,
    HostAttachment? Attachment)
{
    internal static HostTransitionResult Failed(PromotionFailure failure) =>
        new(false, failure, 0, string.Empty, null);

    internal static HostTransitionResult Started(HostAttachment attachment) =>
        new(
            true,
            PromotionFailure.None,
            attachment.ProcessId,
            attachment.ActiveSourceHash,
            attachment);
}
