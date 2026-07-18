using System.ComponentModel.DataAnnotations;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Requests.Get;

public class GetMapsRequest : IValidatableObject
{

    public string? TripVaultName { get; set; }

    [StringLength(200, ErrorMessage = "PlaceId must not exceed 200 characters.")]
    public string? PlaceId { get; set; }

    [StringLength(500, ErrorMessage = "Data must not exceed 500 characters.")]
    public string? Data { get; set; }

    public SearchQuery? SearchQuery { get; set; }

    [StringLength(100, ErrorMessage = "Geographic location must not exceed 100 characters.")]
    [RegularExpression(@"^@-?\d+(\.\d+)?,-?\d+(\.\d+)?,(3|[4-9]|1\d|2[01])z$",
        ErrorMessage =
            "Geographic location must be in format @latitude,longitude,zoom (e.g., @40.7455096,-74.0083012,14z). Zoom must be between 3z and 21z.")]
    public string? Ll { get; set; }

    [StringLength(10, ErrorMessage = "Type must not exceed 10 characters.")]
    [RegularExpression("^(search|place)$", ErrorMessage = "Type must be either 'search' or 'place'.")]
    public string? Type { get; set; }

    public Localization? Localization { get; set; }

    public MapsPagination? Pagination { get; set; }

    public bool? NoCache { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var parameterCount = new[] { PlaceId, Data, SearchQuery?.Q }.Count(p => !string.IsNullOrEmpty(p));

        if (parameterCount == 0)
        {
            yield return new ValidationResult("One of PlaceId, Data, or SearchQuery parameter must be provided.",
                [nameof(PlaceId), nameof(Data), nameof(SearchQuery)]);
        }

        if (parameterCount > 1)
        {
            yield return new ValidationResult(
                "Only one of PlaceId, Data, or SearchQuery parameters can be used in the same request.",
                [nameof(PlaceId), nameof(Data), nameof(SearchQuery)]);
        }

        if (!string.IsNullOrEmpty(SearchQuery?.Q))
        {
            if (string.IsNullOrEmpty(Type) || Type != "search")
            {
                yield return new ValidationResult("Type must be set to 'search' when using SearchQuery parameter.",
                    [nameof(Type)]);
            }
        }

        if (!string.IsNullOrEmpty(Data))
        {
            if (string.IsNullOrEmpty(Type) || Type != "place")
            {
                yield return new ValidationResult("Type must be set to 'place' when using Data parameter.",
                    [nameof(Type)]);
            }
        }

        if (!string.IsNullOrEmpty(PlaceId) && !string.IsNullOrEmpty(Type))
        {
            yield return new ValidationResult("Type parameter is not required when using PlaceId.", [nameof(Type)]);
        }

        if (!string.IsNullOrEmpty(Ll) && string.IsNullOrEmpty(SearchQuery?.Q))
        {
            yield return new ValidationResult(
                "Geographic location (Ll) parameter should only be used when Type is set to 'search' with SearchQuery parameter.",
                [nameof(Ll)]);
        }

        if (Pagination?.Start > 0 && !string.IsNullOrEmpty(SearchQuery?.Q) && string.IsNullOrEmpty(Ll))
        {
            yield return new ValidationResult(
                "Geographic location (Ll) parameter is required when using pagination with search queries.",
                [nameof(Ll), nameof(Pagination)]);
        }
    }
}

