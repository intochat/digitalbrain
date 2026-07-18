using MediatR;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.Preferences.Queries.GetServices;

public sealed class GetServicesQueryHandler : IRequestHandler<GetServicesQuery, Result<List<ServiceInfoDTO>>>
{
    public Task<Result<List<ServiceInfoDTO>>> Handle(GetServicesQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(ServiceType.GetActivePreferenceServices()
            .Select(serviceType => new ServiceInfoDTO
            {
                Name = serviceType.Name,
                Description = serviceType.Description
            })
            .ToList()));
}
