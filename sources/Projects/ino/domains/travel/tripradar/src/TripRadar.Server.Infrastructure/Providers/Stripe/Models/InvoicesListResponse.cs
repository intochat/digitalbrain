namespace TripRadar.Server.Infrastructure.Providers.Stripe.Models;

/// <summary>
/// Paginated list of invoices.
/// </summary>
public class InvoicesListResponse
{
    /// <summary>List of invoices.</summary>
    public required List<InvoiceResponse> Invoices { get; init; }

    /// <summary>Whether there are more invoices available.</summary>
    public bool HasMore { get; init; }

    /// <summary>ID of the last invoice in the list, for pagination.</summary>
    public string? LastInvoiceId { get; init; }
}
