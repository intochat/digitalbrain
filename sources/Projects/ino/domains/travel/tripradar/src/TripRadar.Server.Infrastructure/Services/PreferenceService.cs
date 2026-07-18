using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.Helpers;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Infrastructure.Contracts;
using PreferenceType = TripRadar.Server.Domain.Entities.PreferenceType;

namespace TripRadar.Server.Infrastructure.Services;

public sealed class PreferenceService(
    IUserPreferencesRepository userPreferencesRepository,
    IPreferenceTypeRepository preferenceTypeRepository,
    IPreferenceMappingService preferenceMappingService,
    IMemoryCache memoryCache,
    ILogger<PreferenceService> logger) : IPreferenceService
{
    private static readonly TimeSpan _preferenceTypesCacheTtl = TimeSpan.FromMinutes(10);
    private const int PreferenceTypesCacheEntrySize = 1;

    public async Task<Result<TRequest>> AddPreferencesAsync<TRequest>(TRequest request, long userId, ServiceType serviceType, CancellationToken cancellationToken = default) where TRequest : class
    {
        try
        {
            var preferenceTypes = await GetActivePreferenceTypesCachedAsync(serviceType, cancellationToken);
            if (preferenceTypes.Count == 0)
            {
                logger.LogWarning("No preference types found for service {ServiceType}", serviceType.Name);
                return Result.Success(request);
            }

            var preferenceTypesById = preferenceTypes.ToDictionary(pt => pt.Id);
            var serviceUserPreferences = await userPreferencesRepository.GetActiveByUserIdAndPreferenceTypeIdsAsync(
                userId,
                preferenceTypesById.Keys.ToArray(),
                cancellationToken);

            if (serviceUserPreferences.Count == 0)
            {
                return Result.Success(request);
            }

            var preference = BuildServicePreferencesObject(serviceType, serviceUserPreferences, preferenceTypesById);
            if (preference == null)
            {
                return Result.Success(request);
            }

            return Result.Success(ApplyPreferences(request, serviceType, preference));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply preferences for user {UserId} and service {ServiceType}", userId, serviceType);
            return Result.Failure<TRequest>(Errors.PreferenceApplication.UnexpectedError);
        }
    }

    private async Task<IReadOnlyList<PreferenceType>> GetActivePreferenceTypesCachedAsync(ServiceType serviceType, CancellationToken cancellationToken)
    {
        var cacheKey = GetPreferenceTypesCacheKey(serviceType.Id);

        if (memoryCache.TryGetValue(cacheKey, out IReadOnlyList<PreferenceType>? cachedPreferenceTypes) &&
            cachedPreferenceTypes is not null)
        {
            return cachedPreferenceTypes;
        }

        var preferenceTypes = await preferenceTypeRepository.GetActiveByServiceTypeAsync(serviceType, cancellationToken);
        IReadOnlyList<PreferenceType> cacheValue = preferenceTypes;
        memoryCache.Set(cacheKey, cacheValue, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _preferenceTypesCacheTtl,
            Size = PreferenceTypesCacheEntrySize
        });

        return cacheValue;
    }

    private static string GetPreferenceTypesCacheKey(int serviceTypeId) => $"preference-types:{serviceTypeId}";

    private object? BuildServicePreferencesObject(
        ServiceType serviceType,
        List<UserPreference> userPreferences,
        IReadOnlyDictionary<int, PreferenceType> preferenceTypesById)
    {
        try
        {
            var preferencesByNormalizedName = new Dictionary<string, (string PreferenceName, object Value, DateTime UpdatedAt)>(StringComparer.OrdinalIgnoreCase);

            foreach (var userPreference in userPreferences)
            {
                if (!preferenceTypesById.TryGetValue(userPreference.PreferenceTypeId, out var preferenceType))
                {
                    continue;
                }

                try
                {
                    var deserializedValue = DeserializePreferenceValue(userPreference.PreferencesJson, preferenceType);
                    if (deserializedValue != null)
                    {
                        var normalizedPreferenceName = NameNormalizer.Normalize(preferenceType.Name);
                        if (!preferencesByNormalizedName.TryGetValue(normalizedPreferenceName, out var existingValue) ||
                            userPreference.UpdatedAt > existingValue.UpdatedAt)
                        {
                            preferencesByNormalizedName[normalizedPreferenceName] = (preferenceType.Name, deserializedValue, userPreference.UpdatedAt);
                        }
                    }
                }
                catch (JsonException ex)
                {
                    logger.LogError(ex, "Failed to deserialize preference {PreferenceName} for user preference {PreferenceId}", preferenceType.Name, userPreference.Id);
                }
            }

            var preferencesDict = preferencesByNormalizedName.Values
                .ToDictionary(value => value.PreferenceName, value => value.Value, StringComparer.OrdinalIgnoreCase);

            return preferencesDict.Count > 0 ? preferencesDict : null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to build preferences object for service {ServiceType}", serviceType.Name);
            return null;
        }
    }

    private object? DeserializePreferenceValue(string preferencesJson, PreferenceType preferenceType)
    {
        try
        {
            var dataTypeName = preferenceType.DataType.Name.ToLowerInvariant();
            return dataTypeName switch
            {
                "string" => JsonSerializer.Deserialize<string>(preferencesJson),
                "int" or "integer" => JsonSerializer.Deserialize<int>(preferencesJson),
                "long" => JsonSerializer.Deserialize<long>(preferencesJson),
                "double" => JsonSerializer.Deserialize<double>(preferencesJson),
                "decimal" => JsonSerializer.Deserialize<decimal>(preferencesJson),
                "bool" or "boolean" => JsonSerializer.Deserialize<bool>(preferencesJson),
                "datetime" => JsonSerializer.Deserialize<DateTime>(preferencesJson),
                "array" => JsonSerializer.Deserialize<string[]>(preferencesJson),
                _ => JsonSerializer.Deserialize<object>(preferencesJson)
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to deserialize preference value {Json} for type {DataType}",
                preferencesJson, preferenceType.DataType.Name);
            return null;
        }
    }

    private TRequest ApplyPreferences<TRequest>(TRequest request, ServiceType serviceType, object preferencesObject) where TRequest : class
    {
        try
        {
            if (preferencesObject is not Dictionary<string, object> preferences)
            {
                logger.LogWarning("Preferences object is not a dictionary for service {ServiceType}", serviceType.Name);
                return request;
            }

            preferenceMappingService.ApplyPreferences(request, preferences);

            return request;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply preferences to request for service {ServiceType}", serviceType.Name);
            return request;
        }
    }
}
