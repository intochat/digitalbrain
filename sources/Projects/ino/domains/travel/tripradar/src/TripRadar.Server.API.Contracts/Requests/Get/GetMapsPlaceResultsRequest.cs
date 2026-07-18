using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Constants;

namespace TripRadar.Server.API.Contracts.Requests.Get;

public class GetMapsPlaceResultsRequest : IValidatableObject
{

    public string? TripVaultName { get; set; }

    [StringLength(200, ErrorMessage = "PlaceId must not exceed 200 characters.")]
    [JsonPropertyName("place_id")]
    public string? PlaceId { get; set; }

    [StringLength(10, ErrorMessage = "Type must not exceed 10 characters.")]
    [RegularExpression("^(place)$", ErrorMessage = "Type must be 'place'.")]
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [StringLength(500, ErrorMessage = "Data must not exceed 500 characters.")]
    [JsonPropertyName("data")]
    public string? Data { get; set; }

    [StringLength(200, ErrorMessage = "DataCid must not exceed 200 characters.")]
    [JsonPropertyName("data_cid")]
    public string? DataCid { get; set; }

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
        if (!string.IsNullOrWhiteSpace(PlaceId))
        {
            if (!string.IsNullOrWhiteSpace(Type) || !string.IsNullOrWhiteSpace(Data) || !string.IsNullOrWhiteSpace(DataCid))
            {
                yield return new ValidationResult(
                    "Type, Data, and DataCid must be omitted when PlaceId is provided.",
                    [nameof(Type), nameof(Data), nameof(DataCid), nameof(PlaceId)]);
            }
        }
        else if (!string.IsNullOrWhiteSpace(DataCid))
        {
            if (!string.IsNullOrWhiteSpace(Type) || !string.IsNullOrWhiteSpace(Data))
            {
                yield return new ValidationResult(
                    "Type and Data must be omitted when DataCid is provided.",
                    [nameof(Type), nameof(Data), nameof(DataCid)]);
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Type) || string.IsNullOrWhiteSpace(Data))
            {
                yield return new ValidationResult(
                    "Type and Data are required when PlaceId or DataCid are not provided.",
                    [nameof(Type), nameof(Data), nameof(PlaceId), nameof(DataCid)]);
            }
        }

        if (NoCache == true && Async == true)
        {
            yield return new ValidationResult(
                "NoCache and Async cannot both be true.",
                [nameof(NoCache), nameof(Async)]);
        }
    }
}

