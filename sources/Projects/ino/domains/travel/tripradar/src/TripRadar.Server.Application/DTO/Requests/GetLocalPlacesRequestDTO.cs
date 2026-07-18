using System.Text.Json.Serialization;
using System.Collections;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.DTO.Requests;

public class GetLocalPlacesRequestDTO : SerpApiBaseRequest, ISerpApiRequest
{
    [JsonPropertyName("searchQuery")]
    public SearchQuery SearchQuery { get; set; } = new();

    [JsonPropertyName("localization")]
    public Localization? Localization { get; set; }

    public GeographicLocation? GeographicLocationDto { get; set; }

    [JsonPropertyName("filters")]
    public LocalPlacesFiltersDTO? Filters { get; set; }

    [Preference(nameof(PreferenceType.Limit))]
    [JsonPropertyName("pagination")]
    public Pagination? Pagination { get; set; }

    [Preference(nameof(PreferenceType.PreferredPlaceTypes))]
    [JsonPropertyName("preferredPlaceTypes")]
    public List<string>? PreferredPlaceTypes { get; set; }

    [Preference(nameof(PreferenceType.SearchRadius))]
    [JsonPropertyName("searchRadius")]
    public double? SearchRadius { get; set; }

    [Preference(nameof(PreferenceType.PreferredPriceLevel))]
    [JsonPropertyName("preferredPriceLevel")]
    public int? PreferredPriceLevel { get; set; }

    [Preference(nameof(PreferenceType.NoTraceMode))]
    [JsonPropertyName("zeroTrace")]
    public bool? ZeroTrace { get; set; }

    public Hashtable GetQueryParams()
    {
        var ht = new Hashtable();

        AddIfNotNull("engine", "google_local", ht);
        AddIfNotNull("q", SearchQuery?.Q, ht);
        AddIfNotNull("hl", Localization?.Hl, ht);
        AddIfNotNull("gl", Localization?.Gl, ht);
        AddIfNotNull("google_domain", Localization?.Domain, ht);

        if (GeographicLocationDto != null)
        {
            AddIfNotNull("location", GeographicLocationDto.Location, ht);
            AddIfNotNull("uule", GeographicLocationDto.Uule, ht);
        }

        if (Filters != null)
        {
            AddIfNotNull("tbs", Filters.Tbs, ht);
        }

        if (Pagination != null)
        {
            AddIfNotNull("start", Pagination.Start, ht);
            AddIfNotNull("num", Pagination.Num, ht);
        }

        if (ZeroTrace == true)
        {
            AddIfNotNull("zero_trace", "true", ht);
        }

        return ht;
    }
}

