using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Create;

public class PaymentHistoryResponse
{
    [JsonPropertyName("payments")]
    [DataMember(Name = "payments")]
    public List<PaymentStatusResponse> Payments { get; set; } = new();

    [JsonPropertyName("totalCount")]
    [DataMember(Name = "totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("page")]
    [DataMember(Name = "page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    [DataMember(Name = "pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("totalPages")]
    [DataMember(Name = "totalPages")]
    public int TotalPages { get; set; }
}
