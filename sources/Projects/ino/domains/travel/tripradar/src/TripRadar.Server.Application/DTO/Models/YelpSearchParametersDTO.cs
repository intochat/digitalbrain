using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class YelpSearchParametersDTO
{
    [JsonPropertyName("engine")] public string? Engine { get; set; }

    [JsonPropertyName("find_desc")] public string? FindDesc { get; set; }

    [JsonPropertyName("find_loc")] public string? FindLoc { get; set; }

    [JsonPropertyName("yelp_domain")] public string? YelpDomain { get; set; }

    [JsonPropertyName("sortby")] public string? SortBy { get; set; }

    [JsonPropertyName("attrs")] public string? Attrs { get; set; }

    [JsonPropertyName("cflt")] public string? Cflt { get; set; }

    [JsonPropertyName("start")] public int? Start { get; set; }
}
