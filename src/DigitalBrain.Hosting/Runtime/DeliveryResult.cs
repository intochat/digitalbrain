namespace DigitalBrain;

[GenerateSerializer]
internal readonly record struct DeliveryResult(bool Delivered, string? Reason, bool Rejected)
{
    internal static DeliveryResult Success { get; } = new(true, null, false);

    internal static DeliveryResult Transient { get; } = new(false, null, false);

    internal static DeliveryResult Terminal(string reason) => new(false, reason, false);

    internal static DeliveryResult Reject(string reason) => new(false, reason, true);
}
