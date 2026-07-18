using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class InvoicesDTO
{
    [JsonPropertyName("invoices")]
    public List<StripeInvoiceInfo> Invoices { get; set; } = [];

    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 20;

    [JsonPropertyName("startingAfter")]
    public string? StartingAfter { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Whether there are more invoices after this page.</summary>
    [JsonPropertyName("hasMore")]
    public bool HasMore { get; set; }

    /// <summary>Cursor pointing to the last invoice in this page, for use as startingAfter in the next request.</summary>
    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }
}
