using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetPlaceReviewsResponse
{
    [JsonPropertyName("search_metadata")] public SearchMetadata SearchMetadata { get; set; } = new();

    [JsonPropertyName("search_parameters")]
    public PlaceReviewsSearchParameters SearchParameters { get; set; } = new();

    [JsonPropertyName("place_info")] public PlaceReviewsPlaceInfo? PlaceInfo { get; set; }

    [JsonPropertyName("topics")] public List<PlaceReviewsTopic>? Topics { get; set; }

    [JsonPropertyName("reviews")] public List<PlaceReview> Reviews { get; set; } = new();

    [JsonPropertyName("pagination")] public PlaceReviewsPaginationResult? Pagination { get; set; }
}