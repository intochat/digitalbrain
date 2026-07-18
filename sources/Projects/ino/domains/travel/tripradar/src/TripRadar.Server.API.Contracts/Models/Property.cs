using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class Property
{
    [JsonPropertyName("type")] public string? Type { get; set; }

    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("description")] public string? Description { get; set; }

    [JsonPropertyName("link")] public string? Link { get; set; }

    [JsonPropertyName("property_token")] public string? PropertyToken { get; set; }

    [JsonPropertyName("serpapi_property_details_link")]
    public string? SerpapiPropertyDetailsLink { get; set; }

    [JsonPropertyName("gps_coordinates")] public GpsCoordinates? GpsCoordinates { get; set; }

    [JsonPropertyName("check_in_time")] public string? CheckInTime { get; set; }

    [JsonPropertyName("check_out_time")] public string? CheckOutTime { get; set; }

    [JsonPropertyName("rate_per_night")] public Rate? RatePerNight { get; set; }

    [JsonPropertyName("total_rate")] public Rate? TotalRate { get; set; }

    [JsonPropertyName("deal")] public string? Deal { get; set; }

    [JsonPropertyName("deal_description")] public string? DealDescription { get; set; }

    [JsonPropertyName("nearby_places")] public List<NearbyPlace>? NearbyPlaces { get; set; }

    [JsonPropertyName("hotel_class")] public string? HotelClass { get; set; }

    [JsonPropertyName("extracted_hotel_class")]
    public int? ExtractedHotelClass { get; set; }

    [JsonPropertyName("images")] public List<Image>? Images { get; set; }

    [JsonPropertyName("overall_rating")] public double? OverallRating { get; set; }

    [JsonPropertyName("reviews")] public int? Reviews { get; set; }

    [JsonPropertyName("ratings")] public List<Rating>? Ratings { get; set; }

    [JsonPropertyName("location_rating")] public double? LocationRating { get; set; }

    [JsonPropertyName("reviews_breakdown")]
    public List<ReviewBreakdown>? ReviewsBreakdown { get; set; }

    [JsonPropertyName("amenities")] public List<string>? Amenities { get; set; }

    [JsonPropertyName("excluded_amenities")]
    public List<string>? ExcludedAmenities { get; set; }

    [JsonPropertyName("essential_info")] public List<string>? EssentialInfo { get; set; }

    [JsonPropertyName("eco_certified")] public bool? EcoCertified { get; set; }

    [JsonPropertyName("prices")] public List<Price>? Prices { get; set; }
}
