using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Options;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Settings;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;
using PreferenceTypeEnum = TripRadar.Server.Domain.Enums.PreferenceType;

namespace TripRadar.Server.Application.UseCases.Users.Commands.UpdatePrivacyMode;

public sealed class UpdatePrivacyModeCommandHandler(
    IUnitOfWork unitOfWork,
    IPreferenceTypeRepository preferenceTypeRepository,
    IUserPreferencesRepository userPreferencesRepository,
    ICacheService cacheService,
    IOptions<CachingSettings> cacheOptions,
    ICurrentUserContext currentUserContext) : IRequestHandler<UpdatePrivacyModeCommand, Result>
{
    private static readonly IReadOnlyCollection<ServiceType> NoTraceServiceTypes =
    [
        ServiceType.Flight,
        ServiceType.Hotel,
        ServiceType.Event,
        ServiceType.LocalPlaces,
        ServiceType.Maps
    ];

    public async Task<Result> Handle(UpdatePrivacyModeCommand request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

        try
        {
            var user = currentUserContext.GetRequiredUser();
            var noTracePreferenceTypes = await preferenceTypeRepository.GetActiveByServiceTypesAndNameAsync(
                NoTraceServiceTypes,
                PreferenceTypeEnum.NoTraceMode.Name,
                cancellationToken);

            if (noTracePreferenceTypes.Count == 0)
            {
                return Result.Success();
            }

            var existingPreferences = await userPreferencesRepository.GetTrackedByUserIdAsync(user.Id, cancellationToken);
            var existingPreferencesByTypeId = existingPreferences.ToDictionary(preference => preference.PreferenceTypeId);
            var preferenceValueJson = JsonSerializer.Serialize(request.Enabled);
            var preferencesToCreate = new List<UserPreference>();

            foreach (var preferenceType in noTracePreferenceTypes)
            {
                if (existingPreferencesByTypeId.TryGetValue(preferenceType.Id, out var existingPreference))
                {
                    existingPreference.UpdatePreferences(preferenceValueJson);
                    existingPreference.SetActive(true);
                    continue;
                }

                preferencesToCreate.Add(new UserPreference(user.Id, preferenceType.Id, preferenceValueJson));
            }
            if (preferencesToCreate.Count > 0)
            {
                await userPreferencesRepository.AddRangeAsync(preferencesToCreate, cancellationToken);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await scope.CommitAsync(cancellationToken);

            var cacheKey = string.Format(System.Globalization.CultureInfo.InvariantCulture, cacheOptions.Value.Preferences.PreferencesCacheKey, request.Username);
            await cacheService.RemoveAsync(cacheKey);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Errors.InternalServerError with { Reason = ex.Message });
        }
    }
}

