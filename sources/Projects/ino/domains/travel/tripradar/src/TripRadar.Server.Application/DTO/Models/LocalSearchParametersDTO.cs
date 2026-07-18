using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class LocalSearchParametersDTO
{
    [JsonPropertyName("engine")]
    public string? Engine { get; set; }

    [JsonPropertyName("q")]
    public string Q { get; set; } = string.Empty;

    [JsonPropertyName("location_requested")]
    public string? LocationRequested { get; set; }

    [JsonPropertyName("location_used")]
    public string? LocationUsed { get; set; }

    [JsonPropertyName("google_domain")]
    public string GoogleDomain { get; set; } = string.Empty;

    [JsonPropertyName("hl")]
    public string Hl { get; set; } = "en";

    [JsonPropertyName("gl")]
    public string Gl { get; set; } = "us";

    [JsonPropertyName("device")]
    public string Device { get; set; } = "desktop";

    [JsonPropertyName("uule")]
    public string? Uule { get; set; }
}
