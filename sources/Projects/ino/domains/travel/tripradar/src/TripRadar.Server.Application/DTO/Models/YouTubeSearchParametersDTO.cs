using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class YouTubeSearchParametersDTO
{
    [JsonPropertyName("engine")]
    public string? Engine { get; set; }

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
}
