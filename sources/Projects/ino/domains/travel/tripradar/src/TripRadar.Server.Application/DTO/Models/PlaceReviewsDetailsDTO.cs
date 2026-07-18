using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class PlaceReviewsDetailsDTO
{
    [JsonPropertyName("service")]
    public object? Service { get; set; } // Can be int or string

    [JsonPropertyName("meal_type")]
    public string? MealType { get; set; }

    [JsonPropertyName("price_per_person")]
    public string? PricePerPerson { get; set; }

    [JsonPropertyName("food")]
    public object? Food { get; set; } // Can be int or string

    [JsonPropertyName("atmosphere")]
    public object? Atmosphere { get; set; } // Can be int or string

    [JsonPropertyName("recommended_dishes")]
    public string? RecommendedDishes { get; set; }

    [JsonPropertyName("vegetarian_options")]
    public string? VegetarianOptions { get; set; }

    [JsonPropertyName("dietary_restrictions")]
    public string? DietaryRestrictions { get; set; }

    [JsonPropertyName("kid_friendliness")]
    public string? KidFriendliness { get; set; }

    [JsonPropertyName("wheelchair_accessibility")]
    public string? WheelchairAccessibility { get; set; }
}
