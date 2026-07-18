using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Contracts.Services;

public interface IInternalTokenService
{
    Task<Result<(decimal TokensDeducted, decimal RemainingTokens, decimal MonthlyLimit, bool LimitReached)>> DeductTokensAsync(
        string username,
        decimal tokensToDeduct,
        ServiceType serviceType,
        UsageEventSourceType sourceType,
        CancellationToken cancellationToken = default);
}
