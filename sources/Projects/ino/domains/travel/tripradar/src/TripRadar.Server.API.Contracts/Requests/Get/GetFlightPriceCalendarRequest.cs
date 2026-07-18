using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Requests.Get;

public class GetFlightPriceCalendarRequest : IValidatableObject
{
    [JsonPropertyName("departure_id")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Departure ID is required.")]
    public required string DepartureId { get; set; }

    [JsonPropertyName("arrival_id")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Arrival ID is required.")]
    public required string ArrivalId { get; set; }

    [JsonPropertyName("year")]
    [Required]
    public required int Year { get; set; }

    [JsonPropertyName("month")]
    [Required]
    public required int Month { get; set; }

    [JsonPropertyName("gl")]
    public string? Gl { get; set; }

    [JsonPropertyName("hl")]
    public string? Hl { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("trip_length_days")]
    public int? TripLengthDays { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(DepartureId))
            yield return new ValidationResult("Departure ID is required.", [nameof(DepartureId)]);

        if (string.IsNullOrWhiteSpace(ArrivalId))
            yield return new ValidationResult("Arrival ID is required.", [nameof(ArrivalId)]);

        if (Month is < 1 or > 12)
            yield return new ValidationResult("Month must be between 1 and 12.", [nameof(Month)]);

        if (Year < 2024 || Year > 2030)
            yield return new ValidationResult("Year must be between 2024 and 2030.", [nameof(Year)]);

        if (TripLengthDays is < 1 or > 30)
            yield return new ValidationResult("Trip length must be between 1 and 30 days.", [nameof(TripLengthDays)]);
    }
}
