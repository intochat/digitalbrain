using MediatR;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Commands.DeletePromoCode;

public record DeletePromoCodeCommand(string Code) : IRequest<Result>, IMonitoringService
{
    public void IncrementCount(CountMetric countMetric)
    {
    }

    public void DecrementCount(CountMetric countMetric)
    {
    }
}
