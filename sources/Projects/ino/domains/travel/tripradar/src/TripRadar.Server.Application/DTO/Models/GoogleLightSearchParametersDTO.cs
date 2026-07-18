using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class GoogleLightSearchParametersDTO
{
    [JsonPropertyName("engine")] public string? Engine { get; set; }

    [JsonPropertyName("q")] public string? Q { get; set; }

    [JsonPropertyName("location")] public string? Location { get; set; }

    [JsonPropertyName("uule")] public string? Uule { get; set; }

    [JsonPropertyName("google_domain")] public string? GoogleDomain { get; set; }

    [JsonPropertyName("gl")] public string? Gl { get; set; }

    [JsonPropertyName("hl")] public string? Hl { get; set; }

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

    [JsonPropertyName("start")] public int? Start { get; set; }

    [JsonPropertyName("device")] public string? Device { get; set; }

    [JsonPropertyName("no_cache")] public bool? NoCache { get; set; }

    [JsonPropertyName("async")] public bool? Async { get; set; }

    [JsonPropertyName("zero_trace")] public bool? ZeroTrace { get; set; }

    [JsonPropertyName("output")] public string? Output { get; set; }

    [JsonPropertyName("json_restrictor")] public string? JsonRestrictor { get; set; }
}
