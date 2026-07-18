using System.Text.Json.Serialization;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.DTO.Responses;

public class GetPlaceReviewsResponseDTO
{
    [JsonPropertyName("search_metadata")]
    public SearchMetadata SearchMetadata { get; set; } = new();

    [JsonPropertyName("search_parameters")]
    public PlaceReviewsSearchParametersDTO SearchParameters { get; set; } = new();

    [JsonPropertyName("place_info")]
    public PlaceReviewsPlaceInfoDTO? PlaceInfo { get; set; }

    [JsonPropertyName("topics")]
    public List<PlaceReviewsTopicDTO>? Topics { get; set; }

    [JsonPropertyName("reviews")]
    public List<PlaceReviewDTO> Reviews { get; set; } = new();

    [JsonPropertyName("pagination")]
    public PlaceReviewsPaginationDTO? Pagination { get; set; }
}