using DigitalBrain.Neurons;

namespace DigitalBrain.Capabilities.Compute;

public enum CostKind
{
    Model = 0,
    Script = 1,
    Http = 2,
    Storage = 3,
    Operation = 4,
    ExternalProvider = 5,
}

// Money is micro-credits: 1 credit = 1e-6 USD. Micros is what was charged, fee included; 0 when the
// work ran locally or on the payer's own connection.
public sealed record Cost
{
    public required string TraceId { get; init; }
    public required string CostId { get; init; }
    public required CostKind Kind { get; init; }
    public required string Item { get; init; }
    public required decimal Quantity { get; init; }
    public required string Unit { get; init; }
    public required long Micros { get; init; }
    public string? Synapse { get; init; }
    public string? App { get; init; }
    public string? Neuron { get; init; }
    public DateTimeOffset At { get; init; }
    public long? ListPriceMicros { get; init; }
    public long? EstimatedListPriceMicros { get; init; }
    public PricingStatus? PricingStatus { get; init; }
    public string? TariffVersion { get; init; }
    public FundingKind? FundingKind { get; init; }
    public ExternalCallOutcomeKind? Outcome { get; init; }
    public string? InvocationId { get; init; }
    public ExternalCallState? AttemptState { get; init; }
    public string? ProviderRequestId { get; init; }
    public ExternalChargePolicy? ChargePolicy { get; init; }
}
