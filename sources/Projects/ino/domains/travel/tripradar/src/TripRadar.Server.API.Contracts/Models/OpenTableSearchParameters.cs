using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class OpenTableSearchParameters
{
    [JsonPropertyName("engine")] public string? Engine { get; set; }

    [JsonPropertyName("rid")] public string? Rid { get; set; }

    [JsonPropertyName("open_table_domain")]
    public string? OpenTableDomain { get; set; }

    [JsonPropertyName("page")] public string? Page { get; set; }
}

public class OpenTableSearchInformation
{
    [JsonPropertyName("page")] public int Page { get; set; }

    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
}
