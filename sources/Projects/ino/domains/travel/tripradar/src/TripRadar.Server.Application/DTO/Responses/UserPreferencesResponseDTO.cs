using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.DTO.Responses;

public sealed record UserPreferencesResponseDTO(List<UserPreferenceDTO> Preferences);

