using System.Text.Json.Serialization;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.DTO.Responses;

public class GetMapsResponseDTO
{
    [JsonPropertyName("search_metadata")]
    public SearchMetadata SearchMetadata { get; set; } = new();

    [JsonPropertyName("search_parameters")]
    public MapsSearchParametersDTO SearchParameters { get; set; } = new();

    [JsonPropertyName("local_results")]
    public List<MapsPlaceResultDTO>? LocalResults { get; set; }

    [JsonPropertyName("place_results")]
    public MapsPlaceResultDTO? PlaceResults { get; set; }
}
