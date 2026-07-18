using System.ComponentModel.DataAnnotations;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Requests.Get;

public class GetEventRequest : IValidatableObject
{
    public string? TripVaultName { get; set; }

    [Required] public required SearchQuery Search { get; set; } = new();

    public GeographicLocation? GeographicLocation { get; set; }

    public Localization? Localization { get; set; }

    public EventFilters? Filters { get; set; }

    public Pagination? Pagination { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (GeographicLocation != null && !string.IsNullOrEmpty(GeographicLocation.Location) &&
            !string.IsNullOrEmpty(GeographicLocation.Uule))
        {
            yield return new ValidationResult(
                "Cannot use both Location and Uule parameters together within GeographicLocation.",
                [nameof(GeographicLocation.Location), nameof(GeographicLocation.Uule)]);
        }

        if (Filters?.Htichips?.Any() == true)
        {
            foreach (var htichip in Filters.Htichips.Where(htichip => !IsValidHtichip(htichip)))
            {
                yield return new ValidationResult($"Invalid htichip format: {htichip}", [nameof(Filters.Htichips)]);
            }
        }
    }

    private bool IsValidHtichip(string htichip)
    {
        var validPrefixes = new[] { "date:", "event_type:" };
        return validPrefixes.Any(htichip.StartsWith);
    }
}

