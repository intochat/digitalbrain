namespace DigitalBrain.Compute;

// One rolled-up group on a monthly statement. A recurring line has no intent: it accrues for the
// period on its own (storage, subscriptions) and still appears on the statement once.
[GenerateSerializer, Alias("compute.statement-line")]
public sealed record StatementLine
{
    [Id(0)] public string? AppId { get; init; }
    [Id(1)] public required string Operation { get; init; }
    [Id(2)] public required decimal Compute { get; init; }
    [Id(3)] public required int Count { get; init; }
    [Id(4)] public required bool Recurring { get; init; }
    [Id(5)] public string? MeterId { get; init; }
}

[GenerateSerializer, Alias("compute.monthly-statement")]
public sealed record MonthlyStatement
{
    [Id(0)] public required string AccountId { get; init; }
    [Id(1)] public required string Period { get; init; }
    [Id(2)] public IReadOnlyList<StatementLine> Lines { get; init; } = [];
    [Id(3)] public required decimal TotalCompute { get; init; }
    [Id(4)] public required decimal TotalUsd { get; init; }
    [Id(5)] public required DateTimeOffset GeneratedAt { get; init; }
}

public enum InvoiceStatus
{
    Draft = 0,
    Open = 1,
    Paid = 2,
    Failed = 3,
}

[GenerateSerializer, Alias("compute.invoice")]
public sealed record Invoice
{
    [Id(0)] public required string InvoiceId { get; init; }
    [Id(1)] public required string AccountId { get; init; }
    [Id(2)] public required string Period { get; init; }
    [Id(3)] public required decimal AmountUsd { get; init; }
    [Id(4)] public required string Currency { get; init; }
    [Id(5)] public IReadOnlyList<StatementLine> Lines { get; init; } = [];
}

[GenerateSerializer, Alias("compute.invoice-result")]
public sealed record InvoiceResult
{
    [Id(0)] public required string InvoiceId { get; init; }
    [Id(1)] public required InvoiceStatus Status { get; init; }
    [Id(2)] public string? ProviderReference { get; init; }
    [Id(3)] public string? CheckoutUrl { get; init; }
}

// The Stripe Checkout/Tax adapter shape. Gate G-2 forbids calling Stripe until written
// confirmation is on file, so the registered provider is the deterministic fake. A real adapter
// implements the same interface behind the gate.
public interface IInvoicingProvider
{
    Task<InvoiceResult> CreateAsync(Invoice invoice, CancellationToken cancellationToken = default);
}
