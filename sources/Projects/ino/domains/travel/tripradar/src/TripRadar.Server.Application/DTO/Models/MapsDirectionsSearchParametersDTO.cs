using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsDirectionsSearchParametersDTO
{
    [JsonPropertyName("engine")] public string? Engine { get; set; }

    [JsonPropertyName("start_addr")] public string? StartAddr { get; set; }

    [JsonPropertyName("end_addr")] public string? EndAddr { get; set; }

    [JsonPropertyName("start_data_id")] public string? StartDataId { get; set; }

    [JsonPropertyName("end_data_id")] public string? EndDataId { get; set; }

    [JsonPropertyName("start_coords")] public string? StartCoords { get; set; }

    [JsonPropertyName("end_coords")] public string? EndCoords { get; set; }

    [JsonPropertyName("travel_mode")] public int? TravelMode { get; set; }

    [JsonPropertyName("distance_unit")] public int? DistanceUnit { get; set; }

    [JsonPropertyName("avoid")] public string? Avoid { get; set; }

    [JsonPropertyName("prefer")] public string? Prefer { get; set; }

    [JsonPropertyName("route")] public int? Route { get; set; }

    [JsonPropertyName("time")] public string? Time { get; set; }

    [JsonPropertyName("hl")] public string? Hl { get; set; }

    [JsonPropertyName("gl")] public string? Gl { get; set; }
}
