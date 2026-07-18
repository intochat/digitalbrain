using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Constants;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Requests.Get;

public class GetGoogleLightSearchRequest : IValidatableObject
{

    public string? TripVaultName { get; set; }

    [Required] public required SearchQuery SearchQuery { get; set; } = new();

    public GeographicLocation? GeographicLocation { get; set; }

    public Localization? Localization { get; set; }

    public Pagination? Pagination { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "Query is required.")]
    [StringLength(500, ErrorMessage = "Query must not exceed 500 characters.")]
    [JsonPropertyName("q")]
    public string? Q
    {
        get => SearchQuery.Q;
        set => SearchQuery.Q = value ?? string.Empty;
    }

    [StringLength(200, ErrorMessage = "Location must not exceed 200 characters.")]
    [JsonPropertyName("location")]
    public string? Location
    {
        get => GeographicLocation?.Location;
        set
        {
            if (value is null && GeographicLocation is null)
            {
                return;
            }

            GeographicLocation ??= new GeographicLocation();
            GeographicLocation.Location = value;
        }
    }

    [StringLength(500, ErrorMessage = "Uule must not exceed 500 characters.")]
    [JsonPropertyName("uule")]
    public string? Uule
    {
        get => GeographicLocation?.Uule;
        set
        {
            if (value is null && GeographicLocation is null)
            {
                return;
            }

            GeographicLocation ??= new GeographicLocation();
            GeographicLocation.Uule = value;
        }
    }

    [StringLength(100, ErrorMessage = "Google domain must not exceed 100 characters.")]
    [JsonPropertyName("google_domain")]
    public string? GoogleDomain
    {
        get => Localization?.GoogleDomain;
        set
        {
            if (value is null && Localization is null)
            {
                return;
            }

            Localization ??= new Localization();
            Localization.GoogleDomain = value;
        }
    }

    [StringLength(2, MinimumLength = 2, ErrorMessage = "Gl must be exactly 2 characters.")]
    [JsonPropertyName("gl")]
    public string? Gl
    {
        get => Localization?.Gl;
        set
        {
            if (value is null && Localization is null)
            {
                return;
            }

            Localization ??= new Localization();
            Localization.Gl = value;
        }
    }

    [StringLength(2, MinimumLength = 2, ErrorMessage = "Hl must be exactly 2 characters.")]
    [JsonPropertyName("hl")]
    public string? Hl
    {
        get => Localization?.Hl;
        set
        {
            if (value is null && Localization is null)
            {
                return;
            }

            Localization ??= new Localization();
            Localization.Hl = value;
        }
    }

    [StringLength(200, ErrorMessage = "Lr must not exceed 200 characters.")]
    [JsonPropertyName("lr")]
    public string? Lr { get; set; }

    [StringLength(50, ErrorMessage = "AsDt must not exceed 50 characters.")]
    [JsonPropertyName("as_dt")]
    public string? AsDt { get; set; }

    [StringLength(200, ErrorMessage = "AsEpq must not exceed 200 characters.")]
    [JsonPropertyName("as_epq")]
    public string? AsEpq { get; set; }

    [StringLength(200, ErrorMessage = "AsEq must not exceed 200 characters.")]
    [JsonPropertyName("as_eq")]
    public string? AsEq { get; set; }

    [StringLength(200, ErrorMessage = "AsLq must not exceed 200 characters.")]
    [JsonPropertyName("as_lq")]
    public string? AsLq { get; set; }

    [StringLength(50, ErrorMessage = "AsNlo must not exceed 50 characters.")]
    [JsonPropertyName("as_nlo")]
    public string? AsNlo { get; set; }

    [StringLength(50, ErrorMessage = "AsNhi must not exceed 50 characters.")]
    [JsonPropertyName("as_nhi")]
    public string? AsNhi { get; set; }

    [StringLength(200, ErrorMessage = "AsOq must not exceed 200 characters.")]
    [JsonPropertyName("as_oq")]
    public string? AsOq { get; set; }

    [StringLength(200, ErrorMessage = "AsQ must not exceed 200 characters.")]
    [JsonPropertyName("as_q")]
    public string? AsQ { get; set; }

    [StringLength(50, ErrorMessage = "AsQdr must not exceed 50 characters.")]
    [JsonPropertyName("as_qdr")]
    public string? AsQdr { get; set; }

    [StringLength(200, ErrorMessage = "AsRq must not exceed 200 characters.")]
    [JsonPropertyName("as_rq")]
    public string? AsRq { get; set; }

    [StringLength(200, ErrorMessage = "AsSitesearch must not exceed 200 characters.")]
    [JsonPropertyName("as_sitesearch")]
    public string? AsSitesearch { get; set; }

    [RegularExpression("^(active|off)$", ErrorMessage = "Safe must be either 'active' or 'off'.")]
    [JsonPropertyName("safe")]
    public string? Safe { get; set; }

    [JsonPropertyName("nfpr")]
    public bool? Nfpr { get; set; }

    [JsonPropertyName("filter")]
    public bool? Filter { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Start must be zero or a positive number.")]
    [JsonPropertyName("start")]
    public int? Start
    {
        get => Pagination?.Start;
        set
        {
            if (Pagination is null && value.HasValue)
            {
                Pagination = new Pagination();
            }

            if (Pagination != null)
            {
                Pagination.Start = value;
            }
        }
    }

    [RegularExpression("^(desktop|tablet|mobile)$", ErrorMessage = "Device must be 'desktop', 'tablet', or 'mobile'.")]
    [JsonPropertyName("device")]
    public string? Device { get; set; }

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
        if (GeographicLocation != null && !string.IsNullOrWhiteSpace(GeographicLocation.Location) &&
            !string.IsNullOrWhiteSpace(GeographicLocation.Uule))
        {
            yield return new ValidationResult(
                "location and uule cannot both be set.",
                [nameof(GeographicLocation.Location), nameof(GeographicLocation.Uule)]);
        }

        if (NoCache == true && Async == true)
        {
            yield return new ValidationResult(
                "no_cache and async cannot both be true.",
                [nameof(NoCache), nameof(Async)]);
        }
    }
}

