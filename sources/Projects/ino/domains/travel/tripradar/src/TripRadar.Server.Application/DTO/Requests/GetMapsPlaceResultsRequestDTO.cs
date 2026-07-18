using System.Collections;
using System.Text.Json.Serialization;
using TripRadar.Server.Application.Contracts.Requests;

namespace TripRadar.Server.Application.DTO.Requests;

public class GetMapsPlaceResultsRequestDTO : SerpApiBaseRequest, ISerpApiRequest
{
    [JsonPropertyName("type")] public string? Type { get; set; }

    [JsonPropertyName("data")] public string? Data { get; set; }

    [JsonPropertyName("place_id")] public string? PlaceId { get; set; }

    [JsonPropertyName("data_cid")] public string? DataCid { get; set; }

    [JsonPropertyName("gl")] public string? Gl { get; set; }

    [JsonPropertyName("no_cache")] public bool? NoCache { get; set; }

    [JsonPropertyName("async")] public bool? Async { get; set; }

    [JsonPropertyName("zero_trace")] public bool? ZeroTrace { get; set; }

    [JsonPropertyName("output")] public string? Output { get; set; }

    [JsonPropertyName("json_restrictor")] public string? JsonRestrictor { get; set; }

    public Hashtable GetQueryParams()
    {
        var ht = new Hashtable();

        AddIfNotNull("engine", "google_maps", ht);

        if (!string.IsNullOrWhiteSpace(PlaceId))
        {
            AddIfNotNull("place_id", PlaceId, ht);
        }
        else if (!string.IsNullOrWhiteSpace(DataCid))
        {
            AddIfNotNull("data_cid", DataCid, ht);
        }
        else
        {
            AddIfNotNull("type", string.IsNullOrWhiteSpace(Type) ? "place" : Type, ht);
            AddIfNotNull("data", Data, ht);
        }

        AddIfNotNull("gl", Gl, ht);
        if (NoCache == true) AddIfNotNull("no_cache", "true", ht);
        if (Async == true) AddIfNotNull("async", "true", ht);
        if (ZeroTrace == true) AddIfNotNull("zero_trace", "true", ht);
        AddIfNotNull("output", Output, ht);
        AddIfNotNull("json_restrictor", JsonRestrictor, ht);

        return ht;
    }
}

