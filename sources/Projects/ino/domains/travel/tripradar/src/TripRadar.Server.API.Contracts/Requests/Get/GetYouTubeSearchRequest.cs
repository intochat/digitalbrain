using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Constants;

namespace TripRadar.Server.API.Contracts.Requests.Get;

public class GetYouTubeSearchRequest : IValidatableObject
{

    public string? TripVaultName { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "SearchQuery is required.")]
    [StringLength(200, ErrorMessage = "SearchQuery must not exceed 200 characters.")]
    [JsonPropertyName("search_query")]
    public required string SearchQuery { get; set; }

    [StringLength(2, MinimumLength = 2, ErrorMessage = "Gl must be exactly 2 characters.")]
    [JsonPropertyName("gl")]
    public string? Gl { get; set; }

    [StringLength(2, MinimumLength = 2, ErrorMessage = "Hl must be exactly 2 characters.")]
    [JsonPropertyName("hl")]
    public string? Hl { get; set; }

    [StringLength(500, ErrorMessage = "Sp must not exceed 500 characters.")]
    [JsonPropertyName("sp")]
    public string? Sp { get; set; }

    [JsonPropertyName("no_cache")]
    public bool? NoCache { get; set; }

    [JsonPropertyName("async")]
    public bool? Async { get; set; }

    [JsonPropertyName("zero_trace")]
    public bool? ZeroTrace { get; set; }

    [StringLength(10, ErrorMessage = "Output must be either 'json' or 'html'.")]
    [RegularExpression("^(json|html)$", ErrorMessage = "Output must be either 'json' or 'html'.")]
    [JsonPropertyName("output")]
    public string? Output { get; set; }

    [StringLength(2000, ErrorMessage = "JsonRestrictor must not exceed 2000 characters.")]
    [JsonPropertyName("json_restrictor")]
    public string? JsonRestrictor { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (NoCache == true && Async == true)
        {
            yield return new ValidationResult(
                "no_cache and async cannot both be true.",
                [nameof(NoCache), nameof(Async)]);
        }
    }
}

