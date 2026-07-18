using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Constants;

namespace TripRadar.Server.API.Contracts.Requests.Get;

public class GetTripAdvisorPlaceRequest : IValidatableObject
{

    public string? TripVaultName { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "PlaceId is required.")]
    [StringLength(200, ErrorMessage = "PlaceId must not exceed 200 characters.")]
    [JsonPropertyName("place_id")]
    public required string PlaceId { get; set; }

    [StringLength(100, ErrorMessage = "Tripadvisor domain must not exceed 100 characters.")]
    [JsonPropertyName("tripadvisor_domain")]
    public string? TripadvisorDomain { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(PlaceId))
        {
            yield return new ValidationResult("PlaceId is required.", [nameof(PlaceId)]);
        }
    }
}

