namespace DigitalBrain.Compute.Billing;

// Maps a monthly statement to the invoice sent to the provider. The invoice id is derived from the
// account and period, so regenerating a month yields the same invoice instead of a second one.
internal static class BillingService
{
    internal static Invoice InvoiceFor(MonthlyStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        return new Invoice
        {
            InvoiceId = $"inv:{statement.AccountId}:{statement.Period}",
            AccountId = statement.AccountId,
            Period = statement.Period,
            AmountUsd = statement.TotalUsd,
            Currency = "usd",
            Lines = statement.Lines,
        };
    }
}

// Gate G-2 blocks a real Stripe integration until written confirmation is on file, so the
// registered provider is this deterministic fake. It has the Checkout/Tax adapter shape: a real
// adapter implements IInvoicingProvider and is registered behind the gate. It never calls Stripe.
internal sealed class FakeInvoicingProvider : IInvoicingProvider
{
    public Task<InvoiceResult> CreateAsync(Invoice invoice, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(invoice);
        return Task.FromResult(new InvoiceResult
        {
            InvoiceId = invoice.InvoiceId,
            Status = InvoiceStatus.Open,
            ProviderReference = $"fake-invoice:{invoice.InvoiceId}",
            CheckoutUrl = $"https://checkout.invalid/{invoice.InvoiceId}",
        });
    }
}
