namespace DigitalBrain;

[GenerateSerializer]
internal readonly record struct DeliveryResult(bool Delivered, string? Reason)
{
    internal static DeliveryResult Success { get; } = new(true, null);

    internal static DeliveryResult Transient { get; } = new(false, null);

    internal static DeliveryResult Terminal(string reason) => new(false, reason);
}
