using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses;

public class PaginatedResponse<T>
{
    [JsonPropertyName("items")]
    [DataMember(Name = "items")]
    public IEnumerable<T> Items { get; set; } = [];

    [JsonPropertyName("totalCount")]
    [DataMember(Name = "totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("pageNumber")]
    [DataMember(Name = "pageNumber")]
    public int PageNumber { get; set; }

    [JsonPropertyName("pageSize")]
    [DataMember(Name = "pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("totalPages")]
    [DataMember(Name = "totalPages")]
    public int TotalPages { get; set; }
}
