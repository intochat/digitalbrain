using MediatR;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Commands.ApplyPromoCode;

public record ApplyPromoCodeCommand(
    string Code,
    string Username,
    decimal OrderAmount
) : IRequest<Result<decimal>>, IMonitoringService
{
    public void IncrementCount(CountMetric countMetric)
    {
    }

    public void DecrementCount(CountMetric countMetric)
    {
    }
}
