using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Constants;

namespace TripRadar.Server.API.Contracts.Requests.Get;

public class GetTripAdvisorSearchRequest : IValidatableObject
{

    public string? TripVaultName { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "Query is required.")]
    [StringLength(200, ErrorMessage = "Query must not exceed 200 characters.")]
    [JsonPropertyName("q")]
    public required string Q { get; set; }

    [Range(ValidationConstants.MinLatitude, ValidationConstants.MaxLatitude,
        ErrorMessage = "Latitude must be between -90 and 90 degrees")]
    [JsonPropertyName("lat")]
    public double? Lat { get; set; }

    [Range(ValidationConstants.MinLongitude, ValidationConstants.MaxLongitude,
        ErrorMessage = "Longitude must be between -180 and 180 degrees")]
    [JsonPropertyName("lon")]
    public double? Lon { get; set; }

    [StringLength(100, ErrorMessage = "Tripadvisor domain must not exceed 100 characters.")]
    [JsonPropertyName("tripadvisor_domain")]
    public string? TripadvisorDomain { get; set; }

    [StringLength(10, ErrorMessage = "Ssrc must not exceed 10 characters.")]
    [RegularExpression("^[aArhgvf]$", ErrorMessage = "Ssrc must be one of: a, r, A, h, g, v, f.")]
    [JsonPropertyName("ssrc")]
    public string? Ssrc { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Offset must be zero or a positive number.")]
    [JsonPropertyName("offset")]
    public int? Offset { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Limit must be greater than zero.")]
    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if ((Lat.HasValue && !Lon.HasValue) || (!Lat.HasValue && Lon.HasValue))
        {
            yield return new ValidationResult(
                "Latitude and longitude must be provided together.",
                [nameof(Lat), nameof(Lon)]);
        }
    }
}

