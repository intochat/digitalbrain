using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Contracts.Services;

public interface IUsageEventWriter
{
    Task WriteAsync(
        long userId,
        ServiceType serviceType,
        decimal tokensConsumed,
        UsageEventSourceType sourceType,
        DateTime occurredAtUtc,
        long? tripVaultId,
        CancellationToken cancellationToken = default);
}
