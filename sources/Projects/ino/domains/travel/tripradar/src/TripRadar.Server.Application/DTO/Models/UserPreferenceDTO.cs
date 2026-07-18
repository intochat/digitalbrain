using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed record UserPreferenceDTO(
    [property: JsonPropertyName("preferenceTypeDisplayName")] string PreferenceTypeDisplayName,
    [property: JsonPropertyName("value")] string Value);
