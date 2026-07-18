namespace TripRadar.Server.Application.DTO.Models;

public sealed record CachedUserPreference(int PreferenceTypeId, string? ServiceTypeName, string? PreferenceTypeName, string PreferencesJson, bool IsActive, DateTime UpdatedAt);
