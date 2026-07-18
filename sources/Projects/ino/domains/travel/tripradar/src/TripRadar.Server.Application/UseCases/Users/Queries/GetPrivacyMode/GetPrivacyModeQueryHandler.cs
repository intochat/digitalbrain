using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Options;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Settings;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Application.DTO.Models;
using PreferenceTypeEnum = TripRadar.Server.Domain.Enums.PreferenceType;

namespace TripRadar.Server.Application.UseCases.Users.Queries.GetPrivacyMode;

public sealed class GetPrivacyModeQueryHandler(
    IPreferenceTypeRepository preferenceTypeRepository,
    IUserPreferencesRepository userPreferencesRepository,
    ICacheService cacheService,
    IOptions<CachingSettings> cacheOptions,
    ICurrentUserContext currentUserContext) : IRequestHandler<GetPrivacyModeQuery, Result<bool>>
{
    private static readonly IReadOnlyCollection<ServiceType> NoTraceServiceTypes =
    [
        ServiceType.Flight,
        ServiceType.Hotel,
        ServiceType.Event,
        ServiceType.LocalPlaces,
        ServiceType.Maps
    ];

    public async Task<Result<bool>> Handle(GetPrivacyModeQuery request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();

        var preferenceTypes = await preferenceTypeRepository.GetActiveByServiceTypesAndNameAsync(
            NoTraceServiceTypes,
            PreferenceTypeEnum.NoTraceMode.Name,
            cancellationToken);

        if (preferenceTypes.Count != NoTraceServiceTypes.Count)
        {
            return Result.Success(false);
        }

        var cacheKey = string.Format(System.Globalization.CultureInfo.InvariantCulture, cacheOptions.Value.Preferences.PreferencesCacheKey, request.Username);
        var cachedUserPreferences = await cacheService.GetByKeyAsync<List<CachedUserPreference>>(cacheKey);
        if (cachedUserPreferences is null)
        {
            var userPreferences = await userPreferencesRepository.GetByUserIdAsync(user.Id, cancellationToken);
            cachedUserPreferences = userPreferences.Select(up => new CachedUserPreference(up.PreferenceTypeId, up.PreferenceType.ServiceType.Name, up.PreferenceType.Name, up.PreferencesJson, up.IsActive, up.UpdatedAt)).ToList();
            await cacheService.TrySetAsync(cacheKey, cachedUserPreferences, cacheOptions.Value.Preferences.DefaultTtlMinutes);
        }

        var userPreferencesByTypeId = cachedUserPreferences
            .Where(preference => preference.IsActive)
            .ToDictionary(preference => preference.PreferenceTypeId);

        foreach (var preferenceType in preferenceTypes)
        {
            if (!userPreferencesByTypeId.TryGetValue(preferenceType.Id, out var preference))
            {
                return Result.Success(false);
            }

            if (!TryParsePreferenceBoolean(preference.PreferencesJson, out var enabled) || !enabled)
            {
                return Result.Success(false);
            }
        }

        return Result.Success(true);
    }

    private static bool TryParsePreferenceBoolean(string preferencesJson, out bool value)
    {
        value = false;

        try
        {
            using var jsonDoc = JsonDocument.Parse(preferencesJson);
            var root = jsonDoc.RootElement;

            if (root.ValueKind == JsonValueKind.True || root.ValueKind == JsonValueKind.False)
            {
                value = root.GetBoolean();
                return true;
            }

            if (root.ValueKind == JsonValueKind.String)
            {
                return bool.TryParse(root.GetString(), out value);
            }
        }
        catch
        {
        }

        return bool.TryParse(preferencesJson, out value);
    }
}
