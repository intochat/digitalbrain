using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public class CreateScheduledFlightQueryRequest : IValidatableObject
{
    [JsonPropertyName("departureAirportCode")]
    [DataMember(Name = "departureAirportCode")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Departure airport code airport code is required.")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Departure airport code must be exactly 3 characters.")]
    public string DepartureAirportCode { get; set; } = null!;

    [JsonPropertyName("destinationAirportCode")]
    [DataMember(Name = "destinationAirportCode")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Destination airport code is required.")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Destination airport code must be exactly 3 characters.")]
    public string DestinationAirportCode { get; set; } = null!;

    [JsonPropertyName("departureDate")]
    [DataMember(Name = "departureDate")]
    [Required(ErrorMessage = "Departure date is required.")]
    [DataType(DataType.Date)]
    public DateTime DepartureDate { get; set; }

    [JsonPropertyName("returnDate")]
    [DataMember(Name = "returnDate")]
    [DataType(DataType.Date)]
    public DateTime? ReturnDate { get; set; }

    [JsonPropertyName("additionalParameters")]
    [DataMember(Name = "additionalParameters")]
    public Dictionary<string, object>? AdditionalParameters { get; set; }

    [JsonPropertyName("selectedColumns")]
    [DataMember(Name = "selectedColumns")]
    public IList<QueryColumn>? SelectedColumns { get; set; }

    [JsonPropertyName("nextExecutionTime")]
    [DataMember(Name = "nextExecutionTime")]
    [DataType(DataType.DateTime)]
    [Required(ErrorMessage = "NextExecutionTime is required.")]
    public DateTime NextExecutionTime { get; set; }

    [JsonPropertyName("schedule")]
    [DataMember(Name = "schedule")]
    [StringLength(100, MinimumLength = 5, ErrorMessage = "Schedule must be between 5 and 100 characters.")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Schedule is required.")]
    public string Schedule { get; set; } = null!;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DepartureAirportCode.Equals(DestinationAirportCode, StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult("Origin and destination airports cannot be the same.",
                [nameof(DepartureAirportCode), nameof(DestinationAirportCode)]);
        }

        if (DepartureDate.Date < DateTime.UtcNow.Date)
        {
            yield return new ValidationResult("Departure date must be in the future.", [nameof(DepartureDate)]);
        }

        if (ReturnDate.HasValue && ReturnDate.Value.Date < DepartureDate.Date)
        {
            yield return new ValidationResult("Return date must be after departure date.", [nameof(ReturnDate)]);
        }
    }
}
