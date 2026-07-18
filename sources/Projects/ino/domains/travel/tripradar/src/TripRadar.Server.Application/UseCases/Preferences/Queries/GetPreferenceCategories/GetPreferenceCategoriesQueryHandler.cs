using System.Reflection;
using MediatR;
using Microsoft.Extensions.Options;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Application.Settings;
using TripRadar.Server.Comms.Core.Helpers;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.Preferences.Queries.GetPreferenceCategories;

public sealed class GetPreferenceCategoriesQueryHandler(
    IPreferenceTypeRepository preferenceTypeRepository,
    ICacheService cacheService,
    IOptions<CachingSettings> cacheOptions) : IRequestHandler<GetPreferenceCategoriesQuery, Result<PreferenceCategoriesResponseDTO>>
{
    private static readonly HashSet<string> _editableServiceNames = typeof(UserPreferencePatchRequestDTO)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Select(property => NameNormalizer.Normalize(property.Name))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> _activePreferenceServiceNames = ServiceType.GetActivePreferenceServices()
        .Select(serviceType => NameNormalizer.Normalize(serviceType.Name))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task<Result<PreferenceCategoriesResponseDTO>> Handle(GetPreferenceCategoriesQuery request, CancellationToken cancellationToken)
    {
        var cacheKey = $"{cacheOptions.Value.Preferences.PreferenceCategoriesCacheKey}:v2";
        var cachedCategories = await cacheService.GetByKeyAsync<PreferenceCategoriesResponseDTO>(cacheKey);

        if (cachedCategories != null)
        {
            return Result.Success(cachedCategories);
        }

        var preferenceTypes = await preferenceTypeRepository.GetAllActiveAsync(cancellationToken);

        var categories = preferenceTypes
            .Where(preferenceType => preferenceType.ServiceType.PreferenceCategory != null)
            .Where(preferenceType => preferenceType.ServiceType.PreferenceCategory!.IsActive)
            .Where(preferenceType => _editableServiceNames.Contains(NameNormalizer.Normalize(preferenceType.ServiceType.Name)))
            .Where(preferenceType => _activePreferenceServiceNames.Contains(NameNormalizer.Normalize(preferenceType.ServiceType.Name)))
            .GroupBy(preferenceType => preferenceType.ServiceType.PreferenceCategory!.Id)
            .OrderBy(group => group.Key)
            .Select(categoryGroup => new PreferenceCategoryDTO
            {
                Name = PreferenceCategoryType.GetById(categoryGroup.Key).Name,
                Services = categoryGroup
                    .GroupBy(preferenceType => new
                    {
                        preferenceType.ServiceType.Id,
                        preferenceType.ServiceType.Name
                    })
                    .OrderBy(group => group.Key.Id)
                    .Select(serviceGroup => new PreferenceServiceDTO
                    {
                        ServiceType = serviceGroup.Key.Name,
                        PreferenceTypes = serviceGroup
                            .OrderBy(preferenceType => preferenceType.Name, StringComparer.OrdinalIgnoreCase)
                            .Select(preferenceType => new PreferenceTypeResponseDTO(
                                preferenceType.ServiceType.Name,
                                preferenceType.Name,
                                preferenceType.DataType.Name,
                                preferenceType.ValidationSchema,
                                preferenceType.IsRequired,
                                preferenceType.DefaultValue))
                            .ToList()
                    })
                    .Where(service => service.PreferenceTypes.Count > 0)
                    .ToList()
            })
            .Where(category => category.Services.Count > 0)
            .ToList();

        var response = new PreferenceCategoriesResponseDTO
        {
            Categories = categories
        };

        await cacheService.TrySetAsync(cacheKey, response, cacheOptions.Value.Preferences.DefaultTtlMinutes);
        return Result.Success(response);
    }
}
