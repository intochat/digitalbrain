using MediatR;
using TripRadar.Server.Application.Contracts.Repositories.Models;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.TripVaults.Queries.GetRecentTripSearches;

public sealed record GetRecentTripSearchesQuery(
    string Username,
    ServiceType ServiceType,
    int Limit = 3) : IRequest<Result<IReadOnlyList<RecentSearchItemDetails>>>, IMonitoringService, IAuthorizedRequest
{
    public void IncrementCount(CountMetric countMetric)
    {
    }

    public void DecrementCount(CountMetric countMetric)
    {
    }
}
