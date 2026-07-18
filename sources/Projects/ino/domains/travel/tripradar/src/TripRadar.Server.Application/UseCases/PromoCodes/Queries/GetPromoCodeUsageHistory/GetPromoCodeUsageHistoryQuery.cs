using MediatR;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Queries.GetPromoCodeUsageHistory;

public record GetPromoCodeUsageHistoryQuery(string Code) : IRequest<Result<List<PromoCodeUsage>>>, IMonitoringService
{
    public void IncrementCount(CountMetric countMetric)
    {
    }

    public void DecrementCount(CountMetric countMetric)
    {
    }
}
