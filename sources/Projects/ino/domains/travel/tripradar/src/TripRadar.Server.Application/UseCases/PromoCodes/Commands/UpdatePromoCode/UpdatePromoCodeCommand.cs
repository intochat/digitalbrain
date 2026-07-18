using MediatR;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Commands.UpdatePromoCode;

public record UpdatePromoCodeCommand(
    string Code,
    string? Description,
    int? MaxUsageCount,
    int? MaxUsagePerUser,
    DateTime? StartDate,
    DateTime? EndDate,
    bool? IsActive
) : IRequest<Result>, IMonitoringService
{
    public void IncrementCount(CountMetric countMetric)
    {
    }

    public void DecrementCount(CountMetric countMetric)
    {
    }
}
