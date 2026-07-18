using MediatR;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Repositories.Models;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.ScheduledExecutions.Queries.GetScheduledExecutions;

public sealed record GetScheduledExecutionsQuery(string Username)
    : IRequest<Result<IReadOnlyList<ScheduledExecutionDetails>>>, IMonitoringService, IAuthorizedRequest
{
    public void IncrementCount(CountMetric countMetric) =>
        countMetric.UpdateMetric(MetricConstants.GetScheduledExecutionsRequest, 1, CountMetric.SetResult(true));

    public void DecrementCount(CountMetric countMetric) =>
        countMetric.UpdateMetric(MetricConstants.GetScheduledExecutionsRequest, 1, CountMetric.SetResult(false));
}
