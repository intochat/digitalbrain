using System.ComponentModel.DataAnnotations;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Requests.Get;

public class GetLocalPlacesRequest : IValidatableObject
{

    public string? TripVaultName { get; set; }

    [Required] public required SearchQuery SearchQuery { get; set; } = new();

    public Localization? Localization { get; set; }

    public GeographicLocation? AdvancedParameters { get; set; }

    public LocalPlacesFilters? Filters { get; set; }

    public Pagination? Pagination { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AdvancedParameters != null && !string.IsNullOrWhiteSpace(AdvancedParameters.Location) &&
            !string.IsNullOrWhiteSpace(AdvancedParameters.Uule))
        {
            yield return new ValidationResult("Location and Uule parameters cannot be used together.",
                [nameof(AdvancedParameters.Location), nameof(AdvancedParameters.Uule)]);
        }
    }
}

