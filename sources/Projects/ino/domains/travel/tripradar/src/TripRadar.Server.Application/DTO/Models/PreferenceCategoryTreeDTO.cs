using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class PreferenceCategoryDTO
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("services")]
    public List<PreferenceServiceDTO> Services { get; init; } = [];
}
