using MediatR;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.TripVaults.Commands.RemoveTripItem;

public record RemoveTripItemByUniqueIdCommand(
    string Username,
    Guid TripVaultUniqueId,
    Guid ItemUniqueId) : IRequest<Result<bool>>, IMonitoringService, IAuthorizedRequest
{
    public void IncrementCount(CountMetric countMetric)
    {
    }

    public void DecrementCount(CountMetric countMetric)
    {
    }
}

