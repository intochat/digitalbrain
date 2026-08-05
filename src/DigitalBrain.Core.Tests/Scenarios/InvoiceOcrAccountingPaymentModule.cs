using System.Collections.Immutable;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record DocumentIngested(
    string BlobRef,
    string MimeType,
    string Source) : Synapse;

public sealed record InvoiceLine(string Description, double Amount);

public sealed record InvoiceParsed(
    string InvoiceNumber,
    string Vendor,
    double Total,
    string Currency,
    ImmutableArray<InvoiceLine> Lines,
    double Confidence) : Synapse;

public sealed record BillDraftProposed(
    string InvoiceNumber,
    string Vendor,
    double Total,
    ImmutableArray<string> GlCodes,
    string MatchStatus) : Synapse;

public sealed record InvoiceBillApproved(
    string InvoiceNumber,
    double ApprovedAmount) : Synapse;

public sealed record BillCreated(
    string InvoiceNumber,
    string BillId,
    double Amount) : Synapse;

public sealed record PaymentProposed(
    string InvoiceNumber,
    double Amount,
    string Method) : Synapse;

public sealed record InvoicePaymentApproved(
    string InvoiceNumber,
    double Amount) : Synapse;

public sealed record PaymentExecuted(
    string InvoiceNumber,
    string PaymentId,
    double Amount) : Synapse;

public sealed record DuplicateInvoiceDetected(
    string InvoiceNumber,
    string ExistingBillId) : Synapse;

// AP spine: ingest → parse → draft; bill approval → create + payment propose; pay approval → execute.
public sealed class AccountsPayableDesk : Neuron<AccountsPayableState>,
    INeuron<DocumentIngested>,
    INeuron<InvoiceBillApproved>,
    INeuron<InvoicePaymentApproved>
{
    public Task HandleAsync(DocumentIngested fact, CancellationToken cancellationToken)
    {
        var invoiceNumber = $"INV-{fact.BlobRef.GetHashCode(StringComparison.Ordinal):x6}";
        if (State.SeenInvoiceNumbers.Contains(invoiceNumber))
        {
            Emit(new DuplicateInvoiceDetected(invoiceNumber, State.BillId ?? "unknown"));
            return Task.CompletedTask;
        }

        State.InvoiceNumber = invoiceNumber;
        State.Vendor = "Contoso Supplies";
        State.Total = 1_250.00;
        State.SeenInvoiceNumbers.Add(invoiceNumber);

        Emit(new InvoiceParsed(
            invoiceNumber,
            State.Vendor,
            State.Total,
            Currency: "USD",
            Lines:
            [
                new InvoiceLine("Widget pack", 1_000),
                new InvoiceLine("Shipping", 250),
            ],
            Confidence: 0.93));
        Emit(new BillDraftProposed(
            invoiceNumber,
            State.Vendor,
            State.Total,
            GlCodes: ["6000-supplies", "6100-freight"],
            MatchStatus: "po-matched"));
        return Task.CompletedTask;
    }

    public Task HandleAsync(InvoiceBillApproved fact, CancellationToken cancellationToken)
    {
        if (!string.Equals(State.InvoiceNumber, fact.InvoiceNumber, StringComparison.Ordinal)
            || State.BillId is not null)
        {
            return Task.CompletedTask;
        }

        State.ApprovedAmount = fact.ApprovedAmount;
        State.BillId = $"bill-{fact.InvoiceNumber}";
        Emit(new BillCreated(fact.InvoiceNumber, State.BillId, fact.ApprovedAmount));
        Emit(new PaymentProposed(fact.InvoiceNumber, fact.ApprovedAmount, Method: "ach"));
        return Task.CompletedTask;
    }

    public Task HandleAsync(InvoicePaymentApproved fact, CancellationToken cancellationToken)
    {
        if (!string.Equals(State.InvoiceNumber, fact.InvoiceNumber, StringComparison.Ordinal)
            || State.BillId is null
            || State.Paid)
        {
            return Task.CompletedTask;
        }

        // Payment uses approved amount only — never raw OCR invent.
        var amount = State.ApprovedAmount ?? fact.Amount;
        State.Paid = true;
        Emit(new PaymentExecuted(
            fact.InvoiceNumber,
            PaymentId: $"pay-{fact.InvoiceNumber}",
            Amount: amount));
        return Task.CompletedTask;
    }
}

public sealed class AccountsPayableState
{
    public string? InvoiceNumber { get; set; }
    public string? Vendor { get; set; }
    public double Total { get; set; }
    public double? ApprovedAmount { get; set; }
    public string? BillId { get; set; }
    public bool Paid { get; set; }
#pragma warning disable CA1002, CA2227
    public HashSet<string> SeenInvoiceNumbers { get; set; } = new(StringComparer.Ordinal);
#pragma warning restore CA1002, CA2227
}

// Catalog sinks for invoice ambient pipeline.
public sealed class InvoicePipelineLedger : Neuron,
    INeuron<InvoiceParsed>,
    INeuron<BillDraftProposed>,
    INeuron<BillCreated>,
    INeuron<PaymentProposed>,
    INeuron<PaymentExecuted>,
    INeuron<DuplicateInvoiceDetected>
{
    public Task HandleAsync(InvoiceParsed fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(BillDraftProposed fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(BillCreated fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(PaymentProposed fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(PaymentExecuted fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(DuplicateInvoiceDetected fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
