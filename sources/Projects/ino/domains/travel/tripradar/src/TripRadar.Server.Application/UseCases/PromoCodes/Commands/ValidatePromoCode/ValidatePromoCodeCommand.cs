using MediatR;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Commands.ValidatePromoCode;

public record ValidatePromoCodeCommand(
    string Code,
    string Username
) : IRequest<Result>, IMonitoringService
{
    public void IncrementCount(CountMetric countMetric)
    {
    }

    public void DecrementCount(CountMetric countMetric)
    {
    }
}
