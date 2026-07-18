using System.Collections;
using System.Text.Json.Serialization;
using TripRadar.Server.Application.Contracts.Requests;

namespace TripRadar.Server.Application.DTO.Requests;

public class GetYelpPlaceRequestDTO : SerpApiBaseRequest, ISerpApiRequest
{
    [JsonPropertyName("place_id")] public string? PlaceId { get; set; }

    [JsonPropertyName("yelp_domain")] public string? YelpDomain { get; set; }

    public Hashtable GetQueryParams()
    {
        var ht = new Hashtable();

        AddIfNotNull("engine", "yelp_place", ht);
        AddIfNotNull("place_id", PlaceId, ht);
        AddIfNotNull("yelp_domain", YelpDomain, ht);

        return ht;
    }
}

