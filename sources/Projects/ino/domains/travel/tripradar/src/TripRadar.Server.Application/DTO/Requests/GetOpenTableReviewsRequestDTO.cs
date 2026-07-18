using System.Collections;
using System.Text.Json.Serialization;
using TripRadar.Server.Application.Contracts.Requests;

namespace TripRadar.Server.Application.DTO.Requests;

public class GetOpenTableReviewsRequestDTO : SerpApiBaseRequest, ISerpApiRequest
{
    [JsonPropertyName("rid")] public string? Rid { get; set; }

    [JsonPropertyName("open_table_domain")]
    public string? OpenTableDomain { get; set; }

    [JsonPropertyName("page")] public int? Page { get; set; }

    public Hashtable GetQueryParams()
    {
        var ht = new Hashtable();

        AddIfNotNull("engine", "open_table_reviews", ht);
        AddIfNotNull("rid", Rid, ht);
        AddIfNotNull("open_table_domain", OpenTableDomain, ht);
        AddIfNotNull("page", Page, ht);

        return ht;
    }
}

