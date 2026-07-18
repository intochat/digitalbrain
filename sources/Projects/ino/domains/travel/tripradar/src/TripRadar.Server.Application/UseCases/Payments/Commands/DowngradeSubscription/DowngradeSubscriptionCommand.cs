using MediatR;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Payments.Commands.DowngradeSubscription;

public record DowngradeSubscriptionCommand(string Username, int TargetTierId, int BillingPeriodId) : IRequest<Result>, IMonitoringService, IAuthorizedRequest
{
    public void IncrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.DowngradeSubscription, 1, CountMetric.SetResult(true));
    }

    public void DecrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.DowngradeSubscription, 1, CountMetric.SetResult(false));
    }
}
