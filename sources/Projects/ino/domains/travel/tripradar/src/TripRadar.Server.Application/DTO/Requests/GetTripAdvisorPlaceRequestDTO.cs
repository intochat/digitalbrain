using System.Collections;
using System.Text.Json.Serialization;
using TripRadar.Server.Application.Contracts.Requests;

namespace TripRadar.Server.Application.DTO.Requests;

public class GetTripAdvisorPlaceRequestDTO : SerpApiBaseRequest, ISerpApiRequest
{
    [JsonPropertyName("place_id")] public string? PlaceId { get; set; }

    [JsonPropertyName("tripadvisor_domain")]
    public string? TripadvisorDomain { get; set; }

    public Hashtable GetQueryParams()
    {
        var ht = new Hashtable();

        AddIfNotNull("engine", "tripadvisor_place", ht);
        AddIfNotNull("place_id", PlaceId, ht);
        AddIfNotNull("tripadvisor_domain", TripadvisorDomain, ht);

        return ht;
    }
}

