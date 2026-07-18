using MediatR;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.UseCases.TripVaults.Queries.GetTripQueryHistory;

public record GetTripQueryHistoryQuery(
    string Username,
    Guid TripVaultId,
    int PageNumber = 1,
    int PageSize = 20) : IRequest<Result<(IEnumerable<TripQueryHistory> Items, int TotalCount)>>, IMonitoringService, IAuthorizedRequest
{
    public void IncrementCount(CountMetric countMetric)
    {
    }

    public void DecrementCount(CountMetric countMetric)
    {
    }
}
