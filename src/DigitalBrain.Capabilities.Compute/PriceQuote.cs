namespace DigitalBrain.Capabilities.Compute;

public enum PricingStatus
{
    Unknown = 0,
    Unpriced = 1,
    Known = 2,
}

public enum ExternalChargePolicy
{
    Unspecified = 0,
    EveryDispatchedAttempt = 1,
    SuccessfulResponse = 2,
    Informational = 3,
}

public sealed record PriceQuote
{
    public required string Unit { get; init; }
    public long? MicrosPerUnit { get; init; }
    public string? TariffVersion { get; init; }
    public DateTimeOffset? EffectiveFrom { get; init; }
    public string? Source { get; init; }
    public ExternalChargePolicy ChargePolicy { get; init; }
    public PricingStatus Status => MicrosPerUnit is null ? PricingStatus.Unpriced : PricingStatus.Known;
}

public sealed record ExternalOperationDescriptor(string Name, string Integration, string Unit);
