using MediatR;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Queries.GetPromoCodeByCode;

public record GetPromoCodeByCodeQuery(string Code) : IRequest<Result<PromoCode>>, IMonitoringService
{
    public void IncrementCount(CountMetric countMetric)
    {
    }

    public void DecrementCount(CountMetric countMetric)
    {
    }
}
