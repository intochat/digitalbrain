using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Constants;

namespace TripRadar.Server.API.Contracts.Requests.Get;

public class GetYelpPlaceFullMenuRequest
{

    public string? TripVaultName { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "PlaceId is required.")]
    [StringLength(200, ErrorMessage = "PlaceId must not exceed 200 characters.")]
    [JsonPropertyName("place_id")]
    public required string PlaceId { get; set; }

    [StringLength(100, ErrorMessage = "Yelp domain must not exceed 100 characters.")]
    [JsonPropertyName("yelp_domain")]
    public string? YelpDomain { get; set; }

    [JsonPropertyName("full_menu")]
    public bool? FullMenu { get; set; } = true;

    [StringLength(200, ErrorMessage = "Menu name must not exceed 200 characters.")]
    [JsonPropertyName("menu_name")]
    public string? MenuName { get; set; }
}

