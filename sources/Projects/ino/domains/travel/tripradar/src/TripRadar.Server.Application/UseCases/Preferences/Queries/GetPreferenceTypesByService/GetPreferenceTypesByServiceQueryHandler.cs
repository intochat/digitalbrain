using System.Globalization;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Options;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Application.Settings;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.Preferences.Queries.GetPreferenceTypesByService;

public sealed class GetPreferenceTypesByServiceQueryHandler(
    IPreferenceTypeRepository preferenceTypeRepository,
    IMapper mapper,
    ICacheService cacheService,
    IOptions<CachingSettings> cacheOptions) : IRequestHandler<GetPreferenceTypesByServiceQuery, Result<List<PreferenceTypeResponseDTO>>>
{
    public async Task<Result<List<PreferenceTypeResponseDTO>>> Handle(GetPreferenceTypesByServiceQuery request, CancellationToken cancellationToken)
    {
        var cacheKey = string.Format(CultureInfo.InvariantCulture, $"{cacheOptions.Value.Preferences.ServicePreferenceTypesCacheKey}:v2", request.ServiceType);

        if (ServiceType.GetActivePreferenceServices().All(serviceType => serviceType.Id != request.ServiceType.Id))
        {
            return Result.Success(new List<PreferenceTypeResponseDTO>());
        }

        var cachedResult = await cacheService.GetByKeyAsync<List<PreferenceTypeResponseDTO>>(cacheKey);
        if (cachedResult != null)
        {
            return Result.Success(cachedResult);
        }

        var preferenceTypes = await preferenceTypeRepository.GetActiveByServiceTypeAsync(request.ServiceType, cancellationToken);
        var response = mapper.Map<List<PreferenceTypeResponseDTO>>(preferenceTypes);

        await cacheService.TrySetAsync(cacheKey, response, cacheOptions.Value.Preferences.DefaultTtlMinutes);

        return Result.Success(response);
    }
}
