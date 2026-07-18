using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Constants;

namespace TripRadar.Server.API.Contracts.Requests.Get;

public class GetMapsDirectionsRequest : IValidatableObject
{

    public string? TripVaultName { get; set; }

    [StringLength(500, ErrorMessage = "Start address must not exceed 500 characters.")]
    [JsonPropertyName("start_addr")]
    public string? StartAddr { get; set; }

    [StringLength(500, ErrorMessage = "End address must not exceed 500 characters.")]
    [JsonPropertyName("end_addr")]
    public string? EndAddr { get; set; }

    [StringLength(200, ErrorMessage = "Start data id must not exceed 200 characters.")]
    [JsonPropertyName("start_data_id")]
    public string? StartDataId { get; set; }

    [StringLength(200, ErrorMessage = "End data id must not exceed 200 characters.")]
    [JsonPropertyName("end_data_id")]
    public string? EndDataId { get; set; }

    [StringLength(50, ErrorMessage = "Start coords must not exceed 50 characters.")]
    [RegularExpression(@"^-?\d+(\.\d+)?,-?\d+(\.\d+)?$", ErrorMessage = "Start coords must be in format latitude,longitude.")]
    [JsonPropertyName("start_coords")]
    public string? StartCoords { get; set; }

    [StringLength(50, ErrorMessage = "End coords must not exceed 50 characters.")]
    [RegularExpression(@"^-?\d+(\.\d+)?,-?\d+(\.\d+)?$", ErrorMessage = "End coords must be in format latitude,longitude.")]
    [JsonPropertyName("end_coords")]
    public string? EndCoords { get; set; }

    [Range(0, 9, ErrorMessage = "Travel mode must be between 0 and 9.")]
    [JsonPropertyName("travel_mode")]
    public int? TravelMode { get; set; }

    [Range(0, 1, ErrorMessage = "Distance unit must be 0 (metric) or 1 (imperial).")]
    [JsonPropertyName("distance_unit")]
    public int? DistanceUnit { get; set; }

    [StringLength(200, ErrorMessage = "Avoid must not exceed 200 characters.")]
    [JsonPropertyName("avoid")]
    public string? Avoid { get; set; }

    [StringLength(200, ErrorMessage = "Prefer must not exceed 200 characters.")]
    [JsonPropertyName("prefer")]
    public string? Prefer { get; set; }

    [Range(2, 4, ErrorMessage = "Route must be 2, 3, or 4.")]
    [JsonPropertyName("route")]
    public int? Route { get; set; }

    [StringLength(50, ErrorMessage = "Time must not exceed 50 characters.")]
    [RegularExpression(@"^(depart_at:\d+|arrive_by:\d+|last_available)$",
        ErrorMessage = "Time must be depart_at:<timestamp>, arrive_by:<timestamp>, or last_available.")]
    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [StringLength(5, ErrorMessage = "HL must not exceed 5 characters.")]
    [JsonPropertyName("hl")]
    public string? Hl { get; set; }

    [StringLength(5, ErrorMessage = "GL must not exceed 5 characters.")]
    [JsonPropertyName("gl")]
    public string? Gl { get; set; }

    [JsonPropertyName("no_cache")]
    public bool? NoCache { get; set; }

    [JsonPropertyName("async")]
    public bool? Async { get; set; }

    [JsonPropertyName("zero_trace")]
    public bool? ZeroTrace { get; set; }

    [StringLength(50, ErrorMessage = "Output must not exceed 50 characters.")]
    [JsonPropertyName("output")]
    public string? Output { get; set; }

    [StringLength(200, ErrorMessage = "Json restrictor must not exceed 200 characters.")]
    [JsonPropertyName("json_restrictor")]
    public string? JsonRestrictor { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var startCount = new[] { StartAddr, StartDataId, StartCoords }.Count(value => !string.IsNullOrWhiteSpace(value));
        if (startCount != 1)
        {
            yield return new ValidationResult(
                "Exactly one of StartAddr, StartDataId, or StartCoords must be provided.",
                [nameof(StartAddr), nameof(StartDataId), nameof(StartCoords)]);
        }

        var endCount = new[] { EndAddr, EndDataId, EndCoords }.Count(value => !string.IsNullOrWhiteSpace(value));
        if (endCount != 1)
        {
            yield return new ValidationResult(
                "Exactly one of EndAddr, EndDataId, or EndCoords must be provided.",
                [nameof(EndAddr), nameof(EndDataId), nameof(EndCoords)]);
        }

        if (NoCache == true && Async == true)
        {
            yield return new ValidationResult(
                "NoCache and Async cannot both be true.",
                [nameof(NoCache), nameof(Async)]);
        }

        if (Route.HasValue && TravelMode != 3)
        {
            yield return new ValidationResult(
                "Route is only supported when TravelMode is 3 (transit).",
                [nameof(Route), nameof(TravelMode)]);
        }

        if (!string.IsNullOrWhiteSpace(Time) && TravelMode != 3)
        {
            yield return new ValidationResult(
                "Time is only supported when TravelMode is 3 (transit).",
                [nameof(Time), nameof(TravelMode)]);
        }
    }
}

