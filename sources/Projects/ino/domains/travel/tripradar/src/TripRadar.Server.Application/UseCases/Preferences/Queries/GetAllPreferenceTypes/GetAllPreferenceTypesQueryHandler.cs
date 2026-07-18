using AutoMapper;
using MediatR;
using Microsoft.Extensions.Options;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Application.Settings;
using TripRadar.Server.Comms.Core.SharedKernel;

using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.Preferences.Queries.GetAllPreferenceTypes;

public sealed class GetAllPreferenceTypesQueryHandler(
    IPreferenceTypeRepository preferenceTypeRepository,
    IMapper mapper,
    ICacheService cacheService,
    IOptions<CachingSettings> cacheOptions) : IRequestHandler<GetAllPreferenceTypesQuery, Result<List<PreferenceTypeResponseDTO>>>
{
    public async Task<Result<List<PreferenceTypeResponseDTO>>> Handle(GetAllPreferenceTypesQuery request, CancellationToken cancellationToken)
    {
        var cacheKey = $"{cacheOptions.Value.Preferences.AllPreferenceTypesCacheKey}:v2";
        var cachedResult = await cacheService.GetByKeyAsync<List<PreferenceTypeResponseDTO>>(cacheKey);
        if (cachedResult != null)
        {
            return Result.Success(cachedResult);
        }

        var activeServiceNames = ServiceType.GetActivePreferenceServices()
            .Select(serviceType => serviceType.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var preferenceTypes = await preferenceTypeRepository.GetAllActiveAsync(cancellationToken);
        var response = mapper.Map<List<PreferenceTypeResponseDTO>>(preferenceTypes
            .Where(preferenceType => activeServiceNames.Contains(preferenceType.ServiceType.Name))
            .ToList());
        await cacheService.TrySetAsync(cacheKey, response, cacheOptions.Value.Preferences.DefaultTtlMinutes);
        return Result.Success(response);
    }
}
