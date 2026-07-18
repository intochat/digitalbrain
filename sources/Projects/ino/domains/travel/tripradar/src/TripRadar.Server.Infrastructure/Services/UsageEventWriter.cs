using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Infrastructure.Services;

public class UsageEventWriter(
    IUsageEventRepository usageEventRepository,
    IUnitOfWork unitOfWork) : IUsageEventWriter
{
    public async Task WriteAsync(
        long userId,
        ServiceType serviceType,
        decimal tokensConsumed,
        UsageEventSourceType sourceType,
        DateTime occurredAtUtc,
        long? tripVaultId,
        CancellationToken cancellationToken = default)
    {
        var usageEvent = new UsageEvent(
            userId,
            serviceType.Id,
            sourceType.Id,
            tokensConsumed,
            occurredAtUtc,
            tripVaultId);

        await usageEventRepository.AddAsync(usageEvent, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
