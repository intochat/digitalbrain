using System.Collections;
using System.Text.Json.Serialization;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.DTO.Requests;

public class GetGoogleLightSearchRequestDTO : SerpApiBaseRequest, ISerpApiRequest
{
    [JsonPropertyName("searchQuery")]
    public SearchQuery SearchQuery { get; set; } = new();

    [JsonPropertyName("geographicLocation")]
    public GeographicLocation? GeographicLocation { get; set; }

    [JsonPropertyName("localization")]
    public Localization? Localization { get; set; }

    [JsonPropertyName("pagination")]
    public Pagination? Pagination { get; set; }

    [JsonPropertyName("lr")] public string? Lr { get; set; }

    [JsonPropertyName("as_dt")] public string? AsDt { get; set; }

    [JsonPropertyName("as_epq")] public string? AsEpq { get; set; }

    [JsonPropertyName("as_eq")] public string? AsEq { get; set; }

    [JsonPropertyName("as_lq")] public string? AsLq { get; set; }

    [JsonPropertyName("as_nlo")] public string? AsNlo { get; set; }

    [JsonPropertyName("as_nhi")] public string? AsNhi { get; set; }

    [JsonPropertyName("as_oq")] public string? AsOq { get; set; }

    [JsonPropertyName("as_q")] public string? AsQ { get; set; }

    [JsonPropertyName("as_qdr")] public string? AsQdr { get; set; }

    [JsonPropertyName("as_rq")] public string? AsRq { get; set; }

    [JsonPropertyName("as_sitesearch")] public string? AsSitesearch { get; set; }

    [JsonPropertyName("safe")] public string? Safe { get; set; }

    [JsonPropertyName("nfpr")] public bool? Nfpr { get; set; }

    [JsonPropertyName("filter")] public bool? Filter { get; set; }

    [JsonPropertyName("device")] public string? Device { get; set; }

    [JsonPropertyName("no_cache")] public bool? NoCache { get; set; }

    [JsonPropertyName("async")] public bool? Async { get; set; }

    [JsonPropertyName("zero_trace")] public bool? ZeroTrace { get; set; }

    [JsonPropertyName("output")] public string? Output { get; set; }

    [JsonPropertyName("json_restrictor")] public string? JsonRestrictor { get; set; }

    public Hashtable GetQueryParams()
    {
        var ht = new Hashtable();

        AddIfNotNull("engine", "google_light", ht);
        AddIfNotNull("q", SearchQuery?.Q, ht);

        if (GeographicLocation != null)
        {
            AddIfNotNull("location", GeographicLocation.Location, ht);
            AddIfNotNull("uule", GeographicLocation.Uule, ht);
        }

        if (Localization != null)
        {
            AddIfNotNull("google_domain", Localization.Domain, ht);
            AddIfNotNull("gl", Localization.Gl, ht);
            AddIfNotNull("hl", Localization.Hl, ht);
        }

        AddIfNotNull("lr", Lr, ht);
        AddIfNotNull("as_dt", AsDt, ht);
        AddIfNotNull("as_epq", AsEpq, ht);
        AddIfNotNull("as_eq", AsEq, ht);
        AddIfNotNull("as_lq", AsLq, ht);
        AddIfNotNull("as_nlo", AsNlo, ht);
        AddIfNotNull("as_nhi", AsNhi, ht);
        AddIfNotNull("as_oq", AsOq, ht);
        AddIfNotNull("as_q", AsQ, ht);
        AddIfNotNull("as_qdr", AsQdr, ht);
        AddIfNotNull("as_rq", AsRq, ht);
        AddIfNotNull("as_sitesearch", AsSitesearch, ht);
        AddIfNotNull("safe", Safe, ht);

        if (Nfpr.HasValue)
        {
            AddIfNotNull("nfpr", Nfpr.Value ? "1" : "0", ht);
        }

        if (Filter.HasValue)
        {
            AddIfNotNull("filter", Filter.Value ? "1" : "0", ht);
        }

        if (Pagination?.Start is not null)
        {
            AddIfNotNull("start", Pagination.Start, ht);
        }

        AddIfNotNull("device", Device, ht);

        if (NoCache == true) AddIfNotNull("no_cache", "true", ht);
        if (Async == true) AddIfNotNull("async", "true", ht);
        if (ZeroTrace == true) AddIfNotNull("zero_trace", "true", ht);

        AddIfNotNull("output", Output, ht);
        AddIfNotNull("json_restrictor", JsonRestrictor, ht);

        return ht;
    }
}
