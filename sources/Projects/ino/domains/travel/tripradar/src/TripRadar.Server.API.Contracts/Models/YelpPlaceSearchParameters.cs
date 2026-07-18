using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class YelpPlaceSearchParameters
{
    [JsonPropertyName("engine")] public string? Engine { get; set; }

    [JsonPropertyName("place_id")] public string? PlaceId { get; set; }

    [JsonPropertyName("yelp_domain")] public string? YelpDomain { get; set; }

    [JsonPropertyName("full_menu")] public bool? FullMenu { get; set; }

    [JsonPropertyName("menu_name")] public string? MenuName { get; set; }
}
