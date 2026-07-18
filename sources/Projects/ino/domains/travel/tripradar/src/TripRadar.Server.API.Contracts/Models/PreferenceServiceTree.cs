using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public sealed class PreferenceService
{
    [JsonPropertyName("serviceType")]
    public string ServiceType { get; set; } = string.Empty;

    [JsonPropertyName("preferenceTypes")]
    public List<PreferenceType> PreferenceTypes { get; set; } = [];
}
