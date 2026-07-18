using System.Text.Json.Serialization;
using System.Collections;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.DTO.Requests;

public class GetEventRequestDTO : SerpApiBaseRequest, ISerpApiRequest
{
    public required string Username { get; set; }

    [JsonPropertyName("searchQuery")]
    public SearchQuery SearchQuery { get; set; } = new();

    [JsonPropertyName("geographicLocation")]
    public GeographicLocation? GeographicLocation { get; set; }

    [JsonPropertyName("localization")]
    public Localization? Localization { get; set; }

    [JsonPropertyName("nextPage")]
    public PageToken? NextPage { get; set; }

    [JsonPropertyName("filters")]
    public EventFilters? Filters { get; set; }

    [Preference(nameof(PreferenceType.Language))]
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [Preference(nameof(PreferenceType.PreferredCategories))]
    [JsonPropertyName("preferredCategories")]
    public List<string>? PreferredCategories { get; set; }

    [Preference(nameof(PreferenceType.PreferredEventTypes))]
    [JsonPropertyName("preferredEventTypes")]
    public List<string>? PreferredEventTypes { get; set; }

    [Preference(nameof(PreferenceType.MaxTicketPrice))]
    [JsonPropertyName("maxTicketPrice")]
    public decimal? MaxTicketPrice { get; set; }

    [Preference(nameof(PreferenceType.PreferredVenues))]
    [JsonPropertyName("preferredVenues")]
    public List<string>? PreferredVenues { get; set; }

    [Preference(nameof(PreferenceType.NoTraceMode))]
    [JsonPropertyName("zeroTrace")]
    public bool? ZeroTrace { get; set; }

    public Hashtable GetQueryParams()
    {
        var ht = new Hashtable();

        AddIfNotNull("engine", "google_events", ht);

        var searchQuery = string.IsNullOrWhiteSpace(SearchQuery.Q) ? "events" : SearchQuery.Q.Trim();
        AddIfNotNull("q", GeographicLocation?.Location != null ? $"Events in {GeographicLocation.Location}" : searchQuery, ht);

        AddIfNotNull("hl", Localization?.Hl, ht);
        AddIfNotNull("gl", Localization?.Gl, ht);
        AddIfNotNull("start", NextPage?.NextPageToken, ht);

        if (GeographicLocation != null)
        {
            if (!string.IsNullOrEmpty(GeographicLocation.Location) && string.IsNullOrEmpty(GeographicLocation.Uule))
            {
                AddIfNotNull("location", GeographicLocation.Location, ht);
            }
            else if (string.IsNullOrEmpty(GeographicLocation.Location) && !string.IsNullOrEmpty(GeographicLocation.Uule))
            {
                AddIfNotNull("uule", GeographicLocation.Uule, ht);
            }
        }

        if (Filters?.Htichips?.Any() == true)
        {
            AddIfNotNull("htichips", string.Join(",", Filters.Htichips), ht);
        }

        if (ZeroTrace == true)
        {
            AddIfNotNull("zero_trace", "true", ht);
        }

        return ht;
    }
}
