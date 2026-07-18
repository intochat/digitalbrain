using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Options;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Application.Settings;
using TripRadar.Server.Comms.Core.Helpers;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Mappings;
using TripRadar.Server.Domain.SeedWork;
using PreferenceTypeEnum = TripRadar.Server.Domain.Enums.PreferenceType;

namespace TripRadar.Server.Application.UseCases.Users.Queries.GetUserPreferences;

public sealed class GetUserPreferencesQueryHandler(
    IUserPreferencesRepository userPreferencesRepository,
    ICacheService cacheService,
    IOptions<CachingSettings> cacheOptions,
    ICurrentUserContext currentUserContext) : IRequestHandler<GetUserPreferencesQuery, Result<UserPreferencesResponseDTO>>
{
    private static readonly IReadOnlyDictionary<string, string> _canonicalPreferenceNamesByNormalizedName =
        Enumeration
            .GetAll<PreferenceTypeEnum>()
            .GroupBy(preferenceType => NameNormalizer.Normalize(preferenceType.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);

    public async Task<Result<UserPreferencesResponseDTO>> Handle(GetUserPreferencesQuery request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();

        var cacheKey = string.Format(System.Globalization.CultureInfo.InvariantCulture, cacheOptions.Value.Preferences.PreferencesCacheKey, request.Username);
        var cachedUserPreferences = await cacheService.GetByKeyAsync<List<CachedUserPreference>>(cacheKey);
        if (cachedUserPreferences == null)
        {
            var userPreferences = await userPreferencesRepository.GetByUserIdAsync(user.Id, cancellationToken);
            cachedUserPreferences = userPreferences.Select(up => new CachedUserPreference(up.PreferenceTypeId, up.PreferenceType.ServiceType.Name, up.PreferenceType.Name, up.PreferencesJson, up.IsActive, up.UpdatedAt)).ToList();
            await cacheService.TrySetAsync(cacheKey, cachedUserPreferences, cacheOptions.Value.Preferences.DefaultTtlMinutes);
        }

        var preferencesByDisplayName = new Dictionary<string, (string Value, DateTime UpdatedAt)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, serviceTypeName, preferenceTypeName, preferencesJson, isActive, updatedAt) in cachedUserPreferences)
        {
            if (!isActive || string.IsNullOrEmpty(serviceTypeName) || string.IsNullOrEmpty(preferenceTypeName)) continue;
            var providerName = ServiceTypeToProviderMapping.GetProviderName(serviceTypeName);
            if (providerName == null) continue;

            var preferenceName = ResolveCanonicalPreferenceName(preferenceTypeName);
            var displayName = $"{serviceTypeName}.{preferenceName}";
            var value = ExtractValueFromJson(preferencesJson);

            if (!preferencesByDisplayName.TryGetValue(displayName, out var existingPreference) ||
                updatedAt > existingPreference.UpdatedAt)
            {
                preferencesByDisplayName[displayName] = (value, UpdatedAt: updatedAt);
            }
        }

        var filteredPreferences = preferencesByDisplayName
            .Select(preference => new UserPreferenceDTO(preference.Key, preference.Value.Value))
            .ToList();

        return Result.Success(new UserPreferencesResponseDTO(filteredPreferences));
    }

    private static string ExtractValueFromJson(string preferencesJson)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(preferencesJson);
            var root = jsonDoc.RootElement;

            return root.ValueKind switch
            {
                JsonValueKind.String => root.GetString() ?? string.Empty,
                JsonValueKind.Number => root.GetRawText(),
                JsonValueKind.True or JsonValueKind.False => root.GetBoolean().ToString().ToLowerInvariant(),
                JsonValueKind.Object when root.TryGetProperty("value", out var valueElement) => valueElement.ValueKind
                    switch
                {
                    JsonValueKind.String => valueElement.GetString() ?? string.Empty,
                    JsonValueKind.Number => valueElement.GetRawText(),
                    JsonValueKind.True or JsonValueKind.False => valueElement.GetBoolean().ToString().ToLowerInvariant(),
                    _ => valueElement.GetRawText()
                },
                _ => root.GetRawText()
            };
        }
        catch
        {
            return preferencesJson;
        }
    }

    private static string ResolveCanonicalPreferenceName(string preferenceName)
    {
        var normalizedPreferenceName = NameNormalizer.Normalize(preferenceName);
        return _canonicalPreferenceNamesByNormalizedName.GetValueOrDefault(normalizedPreferenceName, preferenceName);
    }
}
