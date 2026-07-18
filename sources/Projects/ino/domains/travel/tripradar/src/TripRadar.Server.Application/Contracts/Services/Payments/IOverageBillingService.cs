using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Contracts.Services.Payments;

public interface IOverageBillingService
{
    Task<Result<(decimal TokensDeducted, decimal OverageTokensUsed, decimal OverageCharge, bool WarningSent)>>
        DeductTokensWithOverageAsync(User user, ServiceType serviceType, decimal tokensToDeduct,
            CancellationToken cancellationToken = default);

    Task<Result<bool>> IsOverageEligibleAsync(User user, CancellationToken cancellationToken = default);
}
