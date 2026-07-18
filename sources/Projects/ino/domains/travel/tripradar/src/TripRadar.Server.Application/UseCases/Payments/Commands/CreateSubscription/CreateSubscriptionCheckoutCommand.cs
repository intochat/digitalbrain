using MediatR;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Payments.Commands.CreateSubscription;

public record CreateSubscriptionCheckoutCommand(string Username, int TargetTierId, int BillingPeriodId, string? PromoCode = null) : IRequest<Result<SubscriptionCheckoutDto>>, IMonitoringService, IAuthorizedRequest
{
    public void IncrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.CreateSubscriptionCheckout, 1, CountMetric.SetResult(true));
    }

    public void DecrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.CreateSubscriptionCheckout, 1, CountMetric.SetResult(false));
    }
}
