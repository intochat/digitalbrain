using MediatR;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.TripVaults.Commands.CreateTripVault;

public record CreateTripVaultCommand(
    string Username,
    string Name,
    string? Description,
    DateTime? StartDate,
    DateTime? EndDate) : IRequest<Result<Guid>>, IMonitoringService, IAuthorizedRequest
{
    public void IncrementCount(CountMetric countMetric)
    {
    }

    public void DecrementCount(CountMetric countMetric)
    {
    }
}
