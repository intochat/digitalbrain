using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public sealed class GetInvoicesResponse
{
    [JsonPropertyName("invoices")]
    [DataMember(Name = "invoices")]
    public List<InvoiceResponse> Invoices { get; set; } = [];

    [JsonPropertyName("limit")]
    [DataMember(Name = "limit")]
    public int Limit { get; set; } = 20;

    [JsonPropertyName("startingAfter")]
    [DataMember(Name = "startingAfter")]
    public string? StartingAfter { get; set; }

    [JsonPropertyName("status")]
    [DataMember(Name = "status")]
    public string? Status { get; set; }

    /// <summary>Whether there are more invoices after this page.</summary>
    [JsonPropertyName("hasMore")]
    [DataMember(Name = "hasMore")]
    public bool HasMore { get; set; }

    /// <summary>Cursor pointing to the last invoice in this page, for use as startingAfter in the next request.</summary>
    [JsonPropertyName("nextCursor")]
    [DataMember(Name = "nextCursor")]
    public string? NextCursor { get; set; }
}
