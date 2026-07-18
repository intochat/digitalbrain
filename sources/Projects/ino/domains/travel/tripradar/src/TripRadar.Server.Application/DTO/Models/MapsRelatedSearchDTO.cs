using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsRelatedSearchDTO
{
    [JsonPropertyName("search_term")]
    public string? SearchTerm { get; set; }

    [JsonPropertyName("local_results")]
    public List<LocalPlaceResultDTO>? LocalResults { get; set; }
}
