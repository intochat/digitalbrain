using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public class CreateScheduledHotelQueryRequest : IValidatableObject
{
    [JsonPropertyName("location")]
    [DataMember(Name = "location")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Location is required.")]
    public string Location { get; set; } = null!;

    [JsonPropertyName("checkInDate")]
    [DataMember(Name = "checkInDate")]
    [Required(ErrorMessage = "Check-in date is required.")]
    [DataType(DataType.Date)]
    public DateTime CheckInDate { get; set; }

    [JsonPropertyName("checkOutDate")]
    [DataMember(Name = "checkOutDate")]
    [Required(ErrorMessage = "Check-out date is required.")]
    [DataType(DataType.Date)]
    public DateTime CheckOutDate { get; set; }

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
    [Required(ErrorMessage = "Schedule is required.")]
    public string Schedule { get; set; } = null!;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CheckInDate >= CheckOutDate)
        {
            yield return new ValidationResult("Check-in date must be before check-out date.",
                [nameof(CheckInDate), nameof(CheckOutDate)]);
        }
    }
}
