using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Services;

public sealed class TripVaultResolutionService(ITripVaultRepository tripVaultRepository) : ITripVaultResolutionService
{
    public async Task<long?> ResolveTripVaultIdAsync(long ownerId, string? tripVaultName, CancellationToken cancellationToken)
    {
        var vault = await ResolveNamedOrDefaultVaultAsync(ownerId, tripVaultName, cancellationToken);
        return vault?.Id;
    }

    public async Task<Guid?> ResolveTripVaultUniqueIdAsync(long ownerId, string? tripVaultName, bool createDefaultIfMissing, CancellationToken cancellationToken)
    {
        var vault = await ResolveNamedOrDefaultVaultAsync(ownerId, tripVaultName, cancellationToken);
        if (vault is not null)
        {
            return vault.UniqueId;
        }

        if (!createDefaultIfMissing)
        {
            return null;
        }

        var defaultVault = new TripVault(ownerId, TripVaultConstants.DefaultVault);
        await tripVaultRepository.CreateAsync(defaultVault, cancellationToken);
        return defaultVault.UniqueId;
    }

    private async Task<TripVault?> ResolveNamedOrDefaultVaultAsync(long ownerId, string? tripVaultName, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(tripVaultName))
        {
            var namedVault = await tripVaultRepository.GetByOwnerIdAndNameAsync(ownerId, tripVaultName.Trim(), cancellationToken);
            if (namedVault is not null)
            {
                return namedVault;
            }
        }

        return await tripVaultRepository.GetByOwnerIdAndNameAsync(ownerId, TripVaultConstants.DefaultVault, cancellationToken);
    }
}
