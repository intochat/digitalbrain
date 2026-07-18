using System.Text.Json.Serialization;
using System.Collections;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.DTO.Requests;

public class GetMapsRequestDTO : SerpApiBaseRequest, ISerpApiRequest
{
    [JsonPropertyName("placeId")]
    public string? PlaceId { get; set; }

    [JsonPropertyName("data")]
    public string? Data { get; set; }

    [JsonPropertyName("searchQuery")]
    public SearchQuery? SearchQuery { get; set; }

    [JsonPropertyName("ll")]
    public string? Ll { get; set; }

    [Preference(nameof(PreferenceType.Type))]
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("localization")]
    public Localization? Localization { get; set; }

    [JsonPropertyName("pagination")]
    public MapsPaginationDTO? Pagination { get; set; }

    [JsonPropertyName("noCache")]
    public bool? NoCache { get; set; }

    [Preference(nameof(PreferenceType.NoTraceMode))]
    [JsonPropertyName("zeroTrace")]
    public bool? ZeroTrace { get; set; }

    [Preference(nameof(PreferenceType.PreferredTransportModes))]
    [JsonPropertyName("preferredTransportModes")]
    public List<string>? PreferredTransportModes { get; set; }

    [Preference(nameof(PreferenceType.MaxWalkingDistance))]
    [JsonPropertyName("maxWalkingDistance")]
    public double? MaxWalkingDistance { get; set; }

    [Preference(nameof(PreferenceType.PreferredOperators))]
    [JsonPropertyName("preferredOperators")]
    public List<string>? PreferredOperators { get; set; }

    public Hashtable GetQueryParams()
    {
        var ht = new Hashtable();

        AddIfNotNull("engine", "google_maps", ht);

        if (!string.IsNullOrEmpty(PlaceId))
        {
            AddIfNotNull("place_id", PlaceId, ht);
        }
        else if (!string.IsNullOrEmpty(Data))
        {
            AddIfNotNull("type", "place", ht);
            AddIfNotNull("data", Data, ht);
        }
        else if (!string.IsNullOrEmpty(SearchQuery?.Q))
        {
            AddIfNotNull("type", "search", ht);
            AddIfNotNull("q", SearchQuery.Q, ht);
            AddIfNotNull("ll", Ll, ht);
        }

        if (!string.IsNullOrEmpty(Type) && string.IsNullOrEmpty(PlaceId))
        {
            AddIfNotNull("type", Type, ht);
        }

        if (Localization != null)
        {
            AddIfNotNull("hl", Localization.Hl, ht);
            AddIfNotNull("gl", Localization.Gl, ht);
            AddIfNotNull("google_domain", Localization.Domain, ht);
        }

        if (Pagination != null)
        {
            AddIfNotNull("start", Pagination.Start, ht);
            AddIfNotNull("num", Pagination.Num, ht);
        }

        if (NoCache.HasValue && NoCache.Value)
        {
            AddIfNotNull("no_cache", "true", ht);
        }

        if (ZeroTrace == true)
        {
            AddIfNotNull("zero_trace", "true", ht);
        }

        return ht;
    }
}

