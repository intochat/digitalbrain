using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Contracts.Services;

public interface ITierLimitService
{
    Task<bool> HasAllowedTokensAsync(User user, ServiceType serviceType, CancellationToken cancellationToken = default);

    Task<bool> TryReserveTokensAsync(User user, ServiceType serviceType, CancellationToken cancellationToken = default);

    Task AddTokensAsync(User user, ServiceType serviceType, CancellationToken cancellationToken = default);

    Task<(decimal Current, decimal Limit)> GetUserTokenStatusAsync(User user,
        CancellationToken cancellationToken = default);
}
