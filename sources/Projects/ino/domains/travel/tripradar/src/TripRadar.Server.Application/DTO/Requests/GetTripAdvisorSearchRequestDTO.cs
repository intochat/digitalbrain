using System.Collections;
using System.Text.Json.Serialization;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.DTO.Requests;

public class GetTripAdvisorSearchRequestDTO : SerpApiBaseRequest, ISerpApiRequest
{
    [JsonPropertyName("q")] public string? Q { get; set; }

    [JsonPropertyName("tripadvisor_domain")]
    public string? TripadvisorDomain { get; set; }

    [Preference(nameof(PreferenceType.Ssrc))]
    [JsonPropertyName("ssrc")]
    public string? Ssrc { get; set; }

    [JsonPropertyName("offset")] public int? Offset { get; set; }

    [JsonPropertyName("limit")] public int? Limit { get; set; }

    [JsonPropertyName("lat")] public double? Lat { get; set; }

    [JsonPropertyName("lon")] public double? Lon { get; set; }

    public Hashtable GetQueryParams()
    {
        var ht = new Hashtable();

        AddIfNotNull("engine", "tripadvisor", ht);
        AddIfNotNull("q", Q, ht);
        AddIfNotNull("tripadvisor_domain", TripadvisorDomain, ht);
        AddIfNotNull("ssrc", Ssrc, ht);
        AddIfNotNull("offset", Offset, ht);
        AddIfNotNull("limit", Limit, ht);
        AddIfNotNull("lat", Lat, ht);
        AddIfNotNull("lon", Lon, ht);

        return ht;
    }
}

