using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsPlaceResultsSearchParametersDTO
{
    [JsonPropertyName("engine")] public string? Engine { get; set; }

    [JsonPropertyName("type")] public string? Type { get; set; }

    [JsonPropertyName("data")] public string? Data { get; set; }

    [JsonPropertyName("place_id")] public string? PlaceId { get; set; }

    [JsonPropertyName("data_cid")] public string? DataCid { get; set; }

    [JsonPropertyName("gl")] public string? Gl { get; set; }
}
