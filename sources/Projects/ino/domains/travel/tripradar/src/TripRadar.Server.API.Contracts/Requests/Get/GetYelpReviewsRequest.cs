using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Constants;

namespace TripRadar.Server.API.Contracts.Requests.Get;

public class GetYelpReviewsRequest
{

    public string? TripVaultName { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "PlaceId is required.")]
    [StringLength(200, ErrorMessage = "PlaceId must not exceed 200 characters.")]
    [JsonPropertyName("place_id")]
    public required string PlaceId { get; set; }

    [StringLength(100, ErrorMessage = "Yelp domain must not exceed 100 characters.")]
    [JsonPropertyName("yelp_domain")]
    public string? YelpDomain { get; set; }

    [StringLength(10, ErrorMessage = "Language must not exceed 10 characters.")]
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [StringLength(50, ErrorMessage = "Sortby must not exceed 50 characters.")]
    [JsonPropertyName("sortby")]
    public string? SortBy { get; set; }

    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5.")]
    [JsonPropertyName("rating")]
    public int? Rating { get; set; }

    [JsonPropertyName("not_recommended")]
    public bool? NotRecommended { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Start must be zero or a positive number.")]
    [JsonPropertyName("start")]
    public int? Start { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Num must be greater than zero.")]
    [JsonPropertyName("num")]
    public int? Num { get; set; }

    [StringLength(200, ErrorMessage = "Query must not exceed 200 characters.")]
    [JsonPropertyName("q")]
    public string? Q { get; set; }
}

