using System.ComponentModel.DataAnnotations;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Requests.Get;

public class GetPlaceReviewsRequest : IValidatableObject
{

    public string? TripVaultName { get; set; }

    [StringLength(200, ErrorMessage = "PlaceId must not exceed 200 characters.")]
    public string? PlaceId { get; set; }

    [StringLength(200, ErrorMessage = "DataId must not exceed 200 characters.")]
    public string? DataId { get; set; }

    public Localization? Localization { get; set; }

    public PlaceReviewsFilters? Filters { get; set; }

    public PlaceReviewsPagination? Pagination { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrEmpty(PlaceId) && string.IsNullOrEmpty(DataId))
        {
            yield return new ValidationResult("Either PlaceId or DataId must be provided.",
                [nameof(PlaceId), nameof(DataId)]);
        }

        if (!string.IsNullOrEmpty(PlaceId) && !string.IsNullOrEmpty(DataId))
        {
            yield return new ValidationResult("Only one of PlaceId or DataId can be provided, not both.",
                [nameof(PlaceId), nameof(DataId)]);
        }
    }
}

