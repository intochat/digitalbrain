namespace TripRadar.Server.Application.Contracts.Services;

public interface ITripVaultResolutionService
{
    Task<long?> ResolveTripVaultIdAsync(long ownerId, string? tripVaultName, CancellationToken cancellationToken);

    Task<Guid?> ResolveTripVaultUniqueIdAsync(long ownerId, string? tripVaultName, bool createDefaultIfMissing, CancellationToken cancellationToken);
}
