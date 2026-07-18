using System.Collections;
using System.Text.Json.Serialization;
using TripRadar.Server.Application.Contracts.Requests;

namespace TripRadar.Server.Application.DTO.Requests;

public class GetYelpSearchRequestDTO : SerpApiBaseRequest, ISerpApiRequest
{
    [JsonPropertyName("find_desc")] public string? FindDesc { get; set; }

    [JsonPropertyName("find_loc")] public string? FindLoc { get; set; }

    [JsonPropertyName("yelp_domain")] public string? YelpDomain { get; set; }

    [JsonPropertyName("sortby")] public string? SortBy { get; set; }

    [JsonPropertyName("attrs")] public string? Attrs { get; set; }

    [JsonPropertyName("cflt")] public string? Cflt { get; set; }

    [JsonPropertyName("start")] public int? Start { get; set; }

    public Hashtable GetQueryParams()
    {
        var ht = new Hashtable();
        
        AddIfNotNull("engine", "yelp", ht);
        AddIfNotNull("find_desc", FindDesc, ht);
        AddIfNotNull("find_loc", FindLoc, ht);
        AddIfNotNull("yelp_domain", YelpDomain, ht);
        AddIfNotNull("sortby", SortBy, ht);
        AddIfNotNull("attrs", Attrs, ht);
        AddIfNotNull("cflt", Cflt, ht);
        AddIfNotNull("start", Start, ht);

        return ht;
    }
}

