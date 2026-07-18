using System.Collections;
using System.Text.Json.Serialization;
using TripRadar.Server.Application.Contracts.Requests;

namespace TripRadar.Server.Application.DTO.Requests;

public class GetYelpReviewsRequestDTO : SerpApiBaseRequest, ISerpApiRequest
{
    [JsonPropertyName("place_id")] public string? PlaceId { get; set; }

    [JsonPropertyName("yelp_domain")] public string? YelpDomain { get; set; }

    [JsonPropertyName("language")] public string? Language { get; set; }

    [JsonPropertyName("sortby")] public string? SortBy { get; set; }

    [JsonPropertyName("rating")] public int? Rating { get; set; }

    [JsonPropertyName("not_recommended")] public bool? NotRecommended { get; set; }

    [JsonPropertyName("start")] public int? Start { get; set; }

    [JsonPropertyName("num")] public int? Num { get; set; }

    [JsonPropertyName("q")] public string? Q { get; set; }

    public Hashtable GetQueryParams()
    {
        var ht = new Hashtable();

        AddIfNotNull("engine", "yelp_reviews", ht);
        AddIfNotNull("place_id", PlaceId, ht);
        AddIfNotNull("yelp_domain", YelpDomain, ht);
        AddIfNotNull("language", Language, ht);
        AddIfNotNull("sortby", SortBy, ht);
        AddIfNotNull("rating", Rating, ht);
        AddIfNotNull("not_recommended", NotRecommended, ht);
        AddIfNotNull("start", Start, ht);
        AddIfNotNull("num", Num, ht);
        AddIfNotNull("q", Q, ht);

        return ht;
    }
}

