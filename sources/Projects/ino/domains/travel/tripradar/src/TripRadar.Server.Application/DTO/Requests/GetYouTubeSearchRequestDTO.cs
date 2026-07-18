using System.Collections;
using System.Text.Json.Serialization;
using TripRadar.Server.Application.Contracts.Requests;

namespace TripRadar.Server.Application.DTO.Requests;

public class GetYouTubeSearchRequestDTO : SerpApiBaseRequest, ISerpApiRequest
{
    [JsonPropertyName("search_query")]
    public string? SearchQuery { get; set; }

    [JsonPropertyName("gl")]
    public string? Gl { get; set; }

    [JsonPropertyName("hl")]
    public string? Hl { get; set; }

    [JsonPropertyName("sp")]
    public string? Sp { get; set; }

    [JsonPropertyName("no_cache")]
    public bool? NoCache { get; set; }

    [JsonPropertyName("async")]
    public bool? Async { get; set; }

    [JsonPropertyName("zero_trace")]
    public bool? ZeroTrace { get; set; }

    [JsonPropertyName("output")]
    public string? Output { get; set; }

    [JsonPropertyName("json_restrictor")]
    public string? JsonRestrictor { get; set; }

    public Hashtable GetQueryParams()
    {
        var ht = new Hashtable();

        AddIfNotNull("engine", "youtube", ht);
        AddIfNotNull("search_query", SearchQuery, ht);
        AddIfNotNull("gl", Gl, ht);
        AddIfNotNull("hl", Hl, ht);
        AddIfNotNull("sp", Sp, ht);
        if (NoCache == true) AddIfNotNull("no_cache", "true", ht);
        if (Async == true) AddIfNotNull("async", "true", ht);
        if (ZeroTrace == true) AddIfNotNull("zero_trace", "true", ht);
        AddIfNotNull("output", Output, ht);
        AddIfNotNull("json_restrictor", JsonRestrictor, ht);

        return ht;
    }
}

