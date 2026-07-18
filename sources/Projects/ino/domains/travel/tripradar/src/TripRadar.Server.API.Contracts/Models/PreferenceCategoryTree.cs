using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public sealed class PreferenceCategory
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("services")]
    public List<PreferenceService> Services { get; set; } = [];
}
