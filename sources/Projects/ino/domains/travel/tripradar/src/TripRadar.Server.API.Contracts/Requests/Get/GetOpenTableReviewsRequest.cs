using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Constants;

namespace TripRadar.Server.API.Contracts.Requests.Get;

public class GetOpenTableReviewsRequest : IValidatableObject
{

    public string? TripVaultName { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "Rid is required.")]
    [StringLength(200, ErrorMessage = "Rid must not exceed 200 characters.")]
    [JsonPropertyName("rid")]
    public required string Rid { get; set; }

    [StringLength(100, ErrorMessage = "OpenTable domain must not exceed 100 characters.")]
    [JsonPropertyName("open_table_domain")]
    public string? OpenTableDomain { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Page must be greater than or equal to 1.")]
    [JsonPropertyName("page")]
    public int? Page { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Rid))
        {
            yield return new ValidationResult("Rid is required.", [nameof(Rid)]);
        }
    }
}

