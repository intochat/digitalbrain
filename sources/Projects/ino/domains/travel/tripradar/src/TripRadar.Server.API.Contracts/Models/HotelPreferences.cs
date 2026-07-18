using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Enums;

namespace TripRadar.Server.API.Contracts.Models;

public sealed class HotelPreferences : IValidatableObject
{
    [JsonPropertyName("Adults")]
    [DataMember(Name = "Adults")]
    [Range(1, 20, ErrorMessage = "Adults must be between 1 and 20.")]
    public int? Adults { get; set; }

    [JsonPropertyName("Children")]
    [DataMember(Name = "Children")]
    [Range(0, 10, ErrorMessage = "Children must be between 0 and 10.")]
    public int? Children { get; set; }

    [JsonPropertyName("MinPrice")]
    [DataMember(Name = "MinPrice")]
    [Range(1, 1000000, ErrorMessage = "MinPrice must be between 1 and 1,000,000.")]
    public int? MinPrice { get; set; }

    [JsonPropertyName("MaxPrice")]
    [DataMember(Name = "MaxPrice")]
    [Range(1, 1000000, ErrorMessage = "MaxPrice must be between 1 and 1,000,000.")]
    public int? MaxPrice { get; set; }

    [JsonPropertyName("Currency")]
    [DataMember(Name = "Currency")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Currency must be a 3-character currency code.")]
    public string? Currency { get; set; }

    [JsonPropertyName("SortBy")]
    [DataMember(Name = "SortBy")]
    public HotelSortByType? SortBy { get; set; }

    [JsonPropertyName("FreeCancellation")]
    [DataMember(Name = "FreeCancellation")]
    public bool? FreeCancellation { get; set; }

    [JsonPropertyName("Rating")]
    [DataMember(Name = "Rating")]
    public HotelRatingFilterType? Rating { get; set; }

    [JsonPropertyName("NoTraceMode")]
    [DataMember(Name = "NoTraceMode")]
    public bool? NoTraceMode { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinPrice.HasValue && MaxPrice.HasValue && MinPrice > MaxPrice)
        {
            yield return new ValidationResult("MinPrice cannot be greater than MaxPrice.",
                [nameof(MinPrice), nameof(MaxPrice)]);
        }
    }
}
