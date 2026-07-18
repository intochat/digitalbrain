using System.Text.Json.Serialization;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.DTO.Responses;

public sealed class PreferenceCategoriesResponseDTO
{
    [JsonPropertyName("categories")]
    public List<PreferenceCategoryDTO> Categories { get; init; } = [];
}
