using MediatR;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Commands.CreatePromoCode;

public record CreatePromoCodeCommand(
    string Code,
    string? Description,
    DiscountType DiscountType,
    decimal DiscountValue,
    int? MaxUsageCount,
    int MaxUsagePerUser,
    DateTime StartDate,
    DateTime EndDate,
    bool IsActive = true
) : IRequest<Result<long>>, IMonitoringService
{
    public void IncrementCount(CountMetric countMetric)
    {
    }

    public void DecrementCount(CountMetric countMetric)
    {
    }
}
