using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class TripQueryHistoryResponse
{
    [JsonPropertyName("items")]
    [DataMember(Name = "items")]
    public List<TripItemResponse> Items { get; set; } = new();

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
