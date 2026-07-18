using System.Collections;
using System.Text.Json.Serialization;
using TripRadar.Server.Application.Contracts.Requests;

namespace TripRadar.Server.Application.DTO.Requests;

public class GetMapsDirectionsRequestDTO : SerpApiBaseRequest, ISerpApiRequest
{
    [JsonPropertyName("start_addr")] public string? StartAddr { get; set; }

    [JsonPropertyName("end_addr")] public string? EndAddr { get; set; }

    [JsonPropertyName("start_data_id")] public string? StartDataId { get; set; }

    [JsonPropertyName("end_data_id")] public string? EndDataId { get; set; }

    [JsonPropertyName("start_coords")] public string? StartCoords { get; set; }

    [JsonPropertyName("end_coords")] public string? EndCoords { get; set; }

    [JsonPropertyName("travel_mode")] public int? TravelMode { get; set; }

    [JsonPropertyName("distance_unit")] public int? DistanceUnit { get; set; }

    [JsonPropertyName("avoid")] public string? Avoid { get; set; }

    [JsonPropertyName("prefer")] public string? Prefer { get; set; }

    [JsonPropertyName("route")] public int? Route { get; set; }

    [JsonPropertyName("time")] public string? Time { get; set; }

    [JsonPropertyName("hl")] public string? Hl { get; set; }

    [JsonPropertyName("gl")] public string? Gl { get; set; }

    [JsonPropertyName("no_cache")] public bool? NoCache { get; set; }

    [JsonPropertyName("async")] public bool? Async { get; set; }

    [JsonPropertyName("zero_trace")] public bool? ZeroTrace { get; set; }

    [JsonPropertyName("output")] public string? Output { get; set; }

    [JsonPropertyName("json_restrictor")] public string? JsonRestrictor { get; set; }

    public Hashtable GetQueryParams()
    {
        var ht = new Hashtable();

        AddIfNotNull("engine", "google_maps_directions", ht);
        AddIfNotNull("start_addr", StartAddr, ht);
        AddIfNotNull("end_addr", EndAddr, ht);
        AddIfNotNull("start_data_id", StartDataId, ht);
        AddIfNotNull("end_data_id", EndDataId, ht);
        AddIfNotNull("start_coords", StartCoords, ht);
        AddIfNotNull("end_coords", EndCoords, ht);
        AddIfNotNull("travel_mode", TravelMode, ht);
        AddIfNotNull("distance_unit", DistanceUnit, ht);
        AddIfNotNull("avoid", Avoid, ht);
        AddIfNotNull("prefer", Prefer, ht);
        AddIfNotNull("route", Route, ht);
        AddIfNotNull("time", Time, ht);
        AddIfNotNull("hl", Hl, ht);
        AddIfNotNull("gl", Gl, ht);
        if (NoCache == true) AddIfNotNull("no_cache", "true", ht);
        if (Async == true) AddIfNotNull("async", "true", ht);
        if (ZeroTrace == true) AddIfNotNull("zero_trace", "true", ht);
        AddIfNotNull("output", Output, ht);
        AddIfNotNull("json_restrictor", JsonRestrictor, ht);

        return ht;
    }
}

