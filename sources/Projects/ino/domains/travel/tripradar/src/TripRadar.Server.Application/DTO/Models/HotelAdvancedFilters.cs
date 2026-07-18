using System.Text.Json.Serialization;
using TripRadar.Server.Application.DTO.Enums;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.DTO.Models;

public class HotelAdvancedFilters
{
    [Preference(nameof(PreferenceType.SortBy))]
    [JsonPropertyName("sortBy")]
    public HotelSortByType? SortBy { get; set; }

    [Preference(nameof(PreferenceType.MinPrice))]
    [JsonPropertyName("minPrice")]
    public int? MinPrice { get; set; }

    [Preference(nameof(PreferenceType.MaxPrice))]
    [JsonPropertyName("maxPrice")]
    public int? MaxPrice { get; set; }

    [Preference(nameof(PreferenceType.MaxPricePerNight))]
    [JsonPropertyName("maxPricePerNight")]
    public int? MaxPricePerNight { get; set; }

    [JsonPropertyName("propertyTypes")]
    public IEnumerable<object>? PropertyTypes { get; set; }

    [Preference(nameof(PreferenceType.PreferredAmenities))]
    [JsonPropertyName("amenities")]
    public IEnumerable<object>? Amenities { get; set; }

    [Preference(nameof(PreferenceType.Rating))]
    [JsonPropertyName("rating")]
    public HotelRatingFilterType? Rating { get; set; }

    [Preference(nameof(PreferenceType.PreferredStarRating))]
    [JsonPropertyName("preferredStarRating")]
    public int? PreferredStarRating { get; set; }

    [Preference(nameof(PreferenceType.PreferredHotelChains))]
    [JsonPropertyName("brands")]
    public string? Brands { get; set; }

    [JsonPropertyName("hotelClass")]
    public string? HotelClass { get; set; }

    [Preference(nameof(PreferenceType.PreferredRoomType))]
    [JsonPropertyName("preferredRoomType")]
    public string? PreferredRoomType { get; set; }

    [Preference(nameof(PreferenceType.FreeCancellation))]
    [JsonPropertyName("freeCancellation")]
    public bool? FreeCancellation { get; set; }

    [JsonPropertyName("specialOffers")]
    public bool? SpecialOffers { get; set; }

    [JsonPropertyName("ecoCertified")]
    public bool? EcoCertified { get; set; }
}
