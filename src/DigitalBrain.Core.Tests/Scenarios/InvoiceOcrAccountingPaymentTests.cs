using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class InvoiceOcrAccountingPaymentTests(BrainTestClusters clusters)
    : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<AccountsPayableDesk>()
            .AddModule<InvoicePipelineLedger>();

    [Fact(DisplayName =
        "Invoice OCR→pay: DocumentIngested → InvoiceParsed+BillDraft; bill approval → BillCreated+PaymentProposed; pay approval → PaymentExecuted")]
    public async Task DualApprovalGatesBillThenPayment()
    {
        var ct = Cancellation;
        var context = "ap-owner";
        var session = Brain.Session(context);
        var deskId = new NeuronId("accountspayabledesk", context);
        var ledgerId = new NeuronId("invoicepipelineledger", context);
        var blob = "blob://invoices/vendor-1001.pdf";

        await session.EmitAsync(
            new DocumentIngested(blob, MimeType: "application/pdf", Source: "email"),
            ct);

        var afterIngest = await WaitForJournalAsync(
            deskId,
            reading => reading.AllSaid<InvoiceParsed>().Count == 1
                && reading.AllSaid<BillDraftProposed>().Count == 1,
            "parsed + draft after ingest",
            ct);

        Assert.Empty(afterIngest.AllSaid<BillCreated>());
        Assert.Empty(afterIngest.AllSaid<PaymentProposed>());
        Assert.Empty(afterIngest.AllSaid<PaymentExecuted>());

        var parsed = Assert.IsType<InvoiceParsed>(afterIngest.SaidSingle<InvoiceParsed>().Body);
        Assert.Equal("Contoso Supplies", parsed.Vendor);
        Assert.Equal(1_250.00, parsed.Total);
        Assert.Equal(2, parsed.Lines.Length);

        var sessionReading = await ReadAsync(session.Id, ct);
        var ingestSaid = sessionReading.SaidSingle<DocumentIngested>();
        Assert.Equal(new SynapseRef(session.Id, ingestSaid.Position), afterIngest.SaidSingle<InvoiceParsed>().Cause);

        await session.EmitAsync(
            new InvoiceBillApproved(parsed.InvoiceNumber, ApprovedAmount: parsed.Total),
            ct);

        var afterBill = await WaitForJournalAsync(
            deskId,
            reading => reading.AllSaid<BillCreated>().Count == 1
                && reading.AllSaid<PaymentProposed>().Count == 1,
            "bill created and payment proposed after bill approval",
            ct);

        Assert.Empty(afterBill.AllSaid<PaymentExecuted>());
        var bill = Assert.IsType<BillCreated>(afterBill.SaidSingle<BillCreated>().Body);
        Assert.Equal(parsed.InvoiceNumber, bill.InvoiceNumber);
        Assert.Equal(parsed.Total, bill.Amount);

        var sessionAfterBill = await ReadAsync(session.Id, ct);
        var billApprovedSaid = sessionAfterBill.SaidSingle<InvoiceBillApproved>();
        Assert.Equal(
            new SynapseRef(session.Id, billApprovedSaid.Position),
            afterBill.SaidSingle<PaymentProposed>().Cause);

        await session.EmitAsync(
            new InvoicePaymentApproved(parsed.InvoiceNumber, Amount: 999_999),
            ct);

        var afterPay = await WaitForJournalAsync(
            deskId,
            reading => reading.AllSaid<PaymentExecuted>().Count == 1,
            "payment executed after pay approval",
            ct);

        var ledger = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<InvoiceParsed>().Count == 1
                && reading.AllHeard<BillDraftProposed>().Count == 1
                && reading.AllHeard<BillCreated>().Count == 1
                && reading.AllHeard<PaymentProposed>().Count == 1
                && reading.AllHeard<PaymentExecuted>().Count == 1,
            "ledger heard full invoice pipeline",
            ct);

        var paid = Assert.IsType<PaymentExecuted>(afterPay.SaidSingle<PaymentExecuted>().Body);
        // Payment uses approved bill amount, not the (spoofed) pay-approval amount.
        Assert.Equal(parsed.Total, paid.Amount);
        Assert.NotEqual(999_999, paid.Amount);
        Assert.Equal(deskId, ledger.HeardSingle<PaymentExecuted>().Metadata.Source);
    }
}
