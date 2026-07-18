using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface ITripVaultRepository : IRepository<TripVault>
{
    Task<TripVault?> GetByUniqueIdForUpdateAsync(Guid uniqueId, CancellationToken cancellationToken = default);

    Task<TripVault?> GetByUniqueIdWithSingleItemForUpdateAsync(Guid uniqueId, long itemId, CancellationToken cancellationToken = default);

    Task<TripVault?> GetByUniqueIdWithSingleItemByUniqueIdForUpdateAsync(Guid uniqueId, Guid itemUniqueId, CancellationToken cancellationToken = default);

    Task<bool> ExistsByOwnerIdAndNameAsync(long ownerId, string name, CancellationToken cancellationToken = default);

    Task<bool> ExistsByOwnerIdAndNameExcludingVaultAsync(long ownerId, string name, Guid excludedVaultUniqueId, CancellationToken cancellationToken = default);

    Task<IEnumerable<TripVault>> GetByUserIdAsync(long userId, int limit = 100, CancellationToken cancellationToken = default);

    Task<TripVault?> GetDefaultByOwnerIdAsync(long ownerId, CancellationToken cancellationToken = default);

    Task<TripVault?> GetByOwnerIdAndNameAsync(long ownerId, string name, CancellationToken cancellationToken = default);

    Task<TripVault?> GetWithItemsAsync(Guid uniqueId, CancellationToken cancellationToken = default);

    Task<(IEnumerable<TripQueryHistory> Items, int TotalCount, bool VaultExists, bool IsOwner)> GetQueryHistoryAsync(Guid tripVaultUniqueId, long ownerId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TripQueryHistory>> GetRecentQueryHistoryByDefaultVaultAsync(long ownerId, ServiceType serviceType, int limit, CancellationToken cancellationToken = default);

    Task<Dictionary<long, int>> GetItemsCountByVaultIdsAsync(IEnumerable<long> vaultIds, CancellationToken cancellationToken = default);

    Task CreateAsync(TripVault tripVault, CancellationToken cancellationToken = default);
}
