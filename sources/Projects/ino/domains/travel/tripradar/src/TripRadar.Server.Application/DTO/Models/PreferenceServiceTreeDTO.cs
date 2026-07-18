using System.Text.Json.Serialization;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class PreferenceServiceDTO
{
    [JsonPropertyName("serviceType")]
    public string ServiceType { get; init; } = string.Empty;

    [JsonPropertyName("preferenceTypes")]
    public List<PreferenceTypeResponseDTO> PreferenceTypes { get; init; } = [];
}
