using MediatR;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.Settings;
using TripRadar.Server.Comms.Core.Helpers;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;
using PreferenceTypeEntity = TripRadar.Server.Domain.Entities.PreferenceType;

namespace TripRadar.Server.Application.UseCases.Users.Commands.UpdateUserPreferences;

public sealed class UpdateUserPreferencesCommandHandler(
    IUnitOfWork unitOfWork,
    IUserPreferencesRepository userPreferencesRepository,
    IPreferenceTypeRepository preferenceTypeRepository,
    ICacheService cacheService,
    IOptions<CachingSettings> cacheOptions,
    ICurrentUserContext currentUserContext) : IRequestHandler<UpdateUserPreferencesCommand, Result>
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, PropertyInfo>> PropertiesByType = new();

    public async Task<Result> Handle(UpdateUserPreferencesCommand request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);
        try
        {
            var user = currentUserContext.GetRequiredUser();
            var existingLookup = await GetExistingPreferencesLookupAsync(user.Id, cancellationToken);

            var changes = await ProcessAllServicePreferences(request.Preferences!, user.Id, existingLookup, cancellationToken);
            await PersistAndRefreshCacheAsync(request.Username, changes, cancellationToken);

            await scope.CommitAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Errors.InternalServerError with { Reason = ex.Message });
        }
    }

    private async Task<Dictionary<int, UserPreference>> GetExistingPreferencesLookupAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        var existingPreferences = await userPreferencesRepository.GetTrackedByUserIdAsync(userId, cancellationToken);
        return existingPreferences.ToDictionary(preference => preference.PreferenceTypeId);
    }

    private async Task PersistAndRefreshCacheAsync(
        string username,
        (List<UserPreference> updatedPreferences, List<UserPreference> newPreferences, List<UserPreference> deactivatedPreferences) changes,
        CancellationToken cancellationToken)
    {
        await ExecuteBatchOperations(changes.updatedPreferences, changes.newPreferences, changes.deactivatedPreferences, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var cacheKey = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            cacheOptions.Value.Preferences.PreferencesCacheKey,
            username);

        await cacheService.RemoveAsync(cacheKey);
    }

    private async Task<(List<UserPreference> updatedPreferences, List<UserPreference> newPreferences, List<UserPreference> deactivatedPreferences)> ProcessAllServicePreferences(
        UserPreferencePatchRequestDTO preferences,
        long userId,
        Dictionary<int, UserPreference> existingLookup,
        CancellationToken cancellationToken)
    {
        var updatedPreferences = new List<UserPreference>();
        var newPreferences = new List<UserPreference>();
        var deactivatedPreferences = new List<UserPreference>();

        foreach (var operation in BuildServiceProcessingOperations(
                     preferences,
                     userId,
                     existingLookup,
                     updatedPreferences,
                     newPreferences,
                     deactivatedPreferences,
                     cancellationToken))
        {
            await operation();
        }

        return (updatedPreferences, newPreferences, deactivatedPreferences);
    }

    private IReadOnlyList<Func<Task>> BuildServiceProcessingOperations(
        UserPreferencePatchRequestDTO preferences,
        long userId,
        Dictionary<int, UserPreference> existingLookup,
        List<UserPreference> updatedPreferences,
        List<UserPreference> newPreferences,
        List<UserPreference> deactivatedPreferences,
        CancellationToken cancellationToken)
    {
        return
        [
            () => ProcessServicePreferences(preferences.Flight, ServiceType.Flight, userId, existingLookup, updatedPreferences, newPreferences, deactivatedPreferences, cancellationToken),
            () => ProcessServicePreferences(preferences.Hotel, ServiceType.Hotel, userId, existingLookup, updatedPreferences, newPreferences, deactivatedPreferences, cancellationToken),
            () => ProcessServicePreferences(preferences.Event, ServiceType.Event, userId, existingLookup, updatedPreferences, newPreferences, deactivatedPreferences, cancellationToken),
            () => ProcessServicePreferences(preferences.LocalPlaces, ServiceType.LocalPlaces, userId, existingLookup, updatedPreferences, newPreferences, deactivatedPreferences, cancellationToken),
            () => ProcessServicePreferences(preferences.Maps, ServiceType.Maps, userId, existingLookup, updatedPreferences, newPreferences, deactivatedPreferences, cancellationToken),
            () => ProcessServicePreferences(preferences.PlaceReview, ServiceType.PlaceReview, userId, existingLookup, updatedPreferences, newPreferences, deactivatedPreferences, cancellationToken),
            () => ProcessServicePreferences(preferences.TripAdvisorSearch, ServiceType.TripAdvisorSearch, userId, existingLookup, updatedPreferences, newPreferences, deactivatedPreferences, cancellationToken)
        ];
    }

    private async Task ProcessServicePreferences<T>(
        T? servicePreferences,
        ServiceType serviceType,
        long userId,
        Dictionary<int, UserPreference> existingLookup,
        List<UserPreference> updatedPreferences,
        List<UserPreference> newPreferences,
        List<UserPreference> deactivatedPreferences,
        CancellationToken cancellationToken) where T : class
    {
        if (servicePreferences == null)
        {
            return;
        }

        var preferenceTypesByNormalizedName =
            await GetPreferenceTypesByNormalizedNameAsync(serviceType, cancellationToken);
        var propertiesByNormalizedName = GetPropertiesByNormalizedName<T>();

        foreach (var (normalizedPreferenceName, property) in propertiesByNormalizedName)
        {
            if (!preferenceTypesByNormalizedName.TryGetValue(normalizedPreferenceName, out var matchingPreferenceTypes) ||
                matchingPreferenceTypes.Length == 0)
            {
                continue;
            }

            ApplySinglePreferenceProperty(
                servicePreferences,
                property,
                matchingPreferenceTypes,
                userId,
                existingLookup,
                updatedPreferences,
                newPreferences,
                deactivatedPreferences);
        }
    }

    private async Task<Dictionary<string, PreferenceTypeEntity[]>> GetPreferenceTypesByNormalizedNameAsync(
        ServiceType serviceType,
        CancellationToken cancellationToken)
    {
        var preferenceTypes = await preferenceTypeRepository.GetActiveByServiceTypeAsync(serviceType, cancellationToken);
        return preferenceTypes
            .GroupBy(preferenceType => NameNormalizer.Normalize(preferenceType.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, PropertyInfo> GetPropertiesByNormalizedName<T>() where T : class
    {
        return PropertiesByType.GetOrAdd(
            typeof(T),
            static servicePreferencesType =>
            {
                var properties = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in servicePreferencesType.GetProperties())
                {
                    properties[NameNormalizer.Normalize(property.Name)] = property;
                }

                return properties;
            });
    }

    private static void ApplySinglePreferenceProperty<T>(
        T servicePreferences,
        PropertyInfo property,
        PreferenceTypeEntity[] matchingPreferenceTypes,
        long userId,
        Dictionary<int, UserPreference> existingLookup,
        List<UserPreference> updatedPreferences,
        List<UserPreference> newPreferences,
        List<UserPreference> deactivatedPreferences)
    {
        var value = property.GetValue(servicePreferences);
        if (value == null)
        {
            DeactivatePreferences(matchingPreferenceTypes, existingLookup, deactivatedPreferences);
            return;
        }

        var canonicalPreferenceType = SelectCanonicalPreferenceType(matchingPreferenceTypes, property.Name);
        var valueJson = JsonSerializer.Serialize(value);

        if (existingLookup.TryGetValue(canonicalPreferenceType.Id, out var existingPreference))
        {
            existingPreference.UpdatePreferences(valueJson);
            existingPreference.SetActive(true);
            updatedPreferences.Add(existingPreference);
        }
        else
        {
            newPreferences.Add(new UserPreference(userId, canonicalPreferenceType.Id, valueJson));
        }

        DeactivatePreferences(
            matchingPreferenceTypes,
            existingLookup,
            deactivatedPreferences,
            canonicalPreferenceType.Id);
    }

    private static void DeactivatePreferences(
        IEnumerable<PreferenceTypeEntity> preferenceTypes,
        IReadOnlyDictionary<int, UserPreference> existingLookup,
        List<UserPreference> deactivatedPreferences,
        int? exceptPreferenceTypeId = null)
    {
        foreach (var preferenceType in preferenceTypes)
        {
            if (exceptPreferenceTypeId.HasValue && preferenceType.Id == exceptPreferenceTypeId.Value)
            {
                continue;
            }

            if (existingLookup.TryGetValue(preferenceType.Id, out var existingPreference) && existingPreference.IsActive)
            {
                existingPreference.SetActive(false);
                deactivatedPreferences.Add(existingPreference);
            }
        }
    }

    private static PreferenceTypeEntity SelectCanonicalPreferenceType(
        IReadOnlyList<PreferenceTypeEntity> matchingPreferenceTypes,
        string propertyName)
    {
        var exactMatch = matchingPreferenceTypes
            .FirstOrDefault(preferenceType => string.Equals(preferenceType.Name, propertyName, StringComparison.OrdinalIgnoreCase));

        if (exactMatch != null)
        {
            return exactMatch;
        }

        return matchingPreferenceTypes
            .OrderByDescending(preferenceType => preferenceType.UpdatedAt)
            .ThenByDescending(preferenceType => preferenceType.Id)
            .First();
    }

    private async Task ExecuteBatchOperations(
        List<UserPreference> updatedPreferences,
        List<UserPreference> newPreferences,
        List<UserPreference> deactivatedPreferences,
        CancellationToken cancellationToken)
    {
        if (newPreferences.Count > 0)
        {
            await userPreferencesRepository.AddRangeAsync(newPreferences, cancellationToken);
        }
    }
}

