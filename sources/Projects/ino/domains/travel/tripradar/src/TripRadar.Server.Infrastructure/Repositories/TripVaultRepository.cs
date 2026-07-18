using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class TripVaultRepository(TripRadarDbContext dbContext) : Repository<TripVault>(dbContext), ITripVaultRepository
{
    private const string QueryHistoryNavigationName = "QueryHistoryInternal";
    private const int MinUserTripsLimit = 1;
    private const int MaxUserTripsLimit = 500;

    private static readonly Func<TripRadarDbContext, long, int, IAsyncEnumerable<TripVault>> _getByUserIdQuery =
        EF.CompileAsyncQuery(
            (TripRadarDbContext context, long userId, int limit) =>
                context.TripVaults
                    .AsNoTracking()
                    .Where(tv => tv.OwnerId == userId && !EF.Functions.ILike(tv.Name, TripVaultConstants.DefaultVault))
                    .OrderByDescending(tv => tv.CreatedOn)
                    .Take(limit));

    public async Task<TripVault?> GetByUniqueIdForUpdateAsync(Guid uniqueId, CancellationToken cancellationToken = default) =>
       await dbContext.TripVaults.FirstOrDefaultAsync(tv => tv.UniqueId == uniqueId, cancellationToken);

    public async Task<TripVault?> GetByUniqueIdWithSingleItemForUpdateAsync(Guid uniqueId, long itemId, CancellationToken cancellationToken = default) =>
        await GetByUniqueIdWithFilteredItemForUpdateAsync(uniqueId, qh => qh.Id == itemId, cancellationToken);

    public async Task<TripVault?> GetByUniqueIdWithSingleItemByUniqueIdForUpdateAsync(Guid uniqueId, Guid itemUniqueId, CancellationToken cancellationToken = default) =>
        await GetByUniqueIdWithFilteredItemForUpdateAsync(uniqueId, qh => qh.UniqueId == itemUniqueId, cancellationToken);

    public async Task<bool> ExistsByOwnerIdAndNameAsync(long ownerId, string name, CancellationToken cancellationToken = default) =>
        await dbContext.TripVaults
            .AsNoTracking()
            .AnyAsync(
                tv => tv.OwnerId == ownerId && EF.Functions.ILike(tv.Name,  NormalizeNameForLookup(name)),
                cancellationToken);

    public Task<bool> ExistsByOwnerIdAndNameExcludingVaultAsync(long ownerId, string name, Guid excludedVaultUniqueId, CancellationToken cancellationToken = default) =>
        dbContext.TripVaults
            .AsNoTracking()
            .AnyAsync(
                tv => tv.OwnerId == ownerId &&
                      tv.UniqueId != excludedVaultUniqueId &&
                      EF.Functions.ILike(tv.Name, NormalizeNameForLookup(name)),
                cancellationToken);

    public async Task<IEnumerable<TripVault>> GetByUserIdAsync(long userId, int limit = 100, CancellationToken cancellationToken = default) =>
        await _getByUserIdQuery(dbContext, userId, ClampUserTripsLimit(limit)).ToListAsync(cancellationToken);

    public async Task<TripVault?> GetDefaultByOwnerIdAsync(long ownerId, CancellationToken cancellationToken = default) =>
        await QueryByOwner(ownerId)
            .AsNoTracking()
            .OrderByDescending(tv => tv.CreatedOn)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<TripVault?> GetByOwnerIdAndNameAsync(long ownerId, string name, CancellationToken cancellationToken = default) =>
        await QueryByOwner(ownerId)
            .AsNoTracking()
            .Where(tv => EF.Functions.ILike(tv.Name, NormalizeNameForLookup(name)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<TripVault?> GetWithItemsAsync(Guid uniqueId, CancellationToken cancellationToken = default) =>
        await dbContext.TripVaults
            .AsNoTracking()
            .Include(tv => tv.Owner)
                .ThenInclude(o => o.Profile)
            .FirstOrDefaultAsync(tv => tv.UniqueId == uniqueId, cancellationToken);

    public async Task<(IEnumerable<TripQueryHistory> Items, int TotalCount, bool VaultExists, bool IsOwner)> GetQueryHistoryAsync(Guid tripVaultUniqueId, long ownerId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var vault = await dbContext.TripVaults
            .AsNoTracking()
            .Where(tv => tv.UniqueId == tripVaultUniqueId)
            .Select(tv => new { tv.Id, tv.OwnerId })
            .FirstOrDefaultAsync(cancellationToken);

        if (vault is null) return ([], 0, false, false);

        if (vault.OwnerId != ownerId) return ([], 0, true, false);

        var query = dbContext.TripQueryHistories
            .AsNoTracking()
            .Where(qh => qh.TripVaultId == vault.Id)
            .OrderByDescending(qh => qh.CreatedOn);

        var totalCount = await query.CountAsync(cancellationToken);

        if (totalCount == 0)
        {
            return ([], 0, true, true);
        }

        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount, true, true);
    }

    public async Task<IReadOnlyList<TripQueryHistory>> GetRecentQueryHistoryByDefaultVaultAsync(long ownerId, ServiceType serviceType, int limit, CancellationToken cancellationToken = default)
    {
        var normalizedLimit = Math.Clamp(limit, MinUserTripsLimit, 5);
        var defaultVault = await GetByOwnerIdAndNameAsync(ownerId, TripVaultConstants.DefaultVault, cancellationToken);
        if (defaultVault is null)
        {
            return [];
        }

        return await dbContext.TripQueryHistories
            .AsNoTracking()
            .Where(qh => qh.TripVaultId == defaultVault.Id && qh.ServiceTypeId == serviceType.Id)
            .OrderByDescending(qh => qh.CreatedOn)
            .Take(normalizedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<long, int>> GetItemsCountByVaultIdsAsync(IEnumerable<long> vaultIds, CancellationToken cancellationToken = default)
    {
        var vaultIdsArray = vaultIds.Distinct().ToArray();
        if (vaultIdsArray.Length == 0)
        {
            return [];
        }

        return await dbContext.TripQueryHistories
            .AsNoTracking()
            .Where(qh => vaultIdsArray.Contains(qh.TripVaultId))
            .GroupBy(qh => qh.TripVaultId)
            .Select(g => new { VaultId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.VaultId, x => x.Count, cancellationToken);
    }

    public async Task CreateAsync(TripVault tripVault, CancellationToken cancellationToken = default)
    {
        await dbContext.TripVaults.AddAsync(tripVault, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<TripVault> QueryByOwner(long ownerId) =>
        dbContext.TripVaults.Where(tv => tv.OwnerId == ownerId);

    private async Task<TripVault?> GetByUniqueIdWithFilteredItemForUpdateAsync(Guid uniqueId, Expression<Func<TripQueryHistory, bool>> itemPredicate, CancellationToken cancellationToken)
    {
        var vault = await GetByUniqueIdForUpdateAsync(uniqueId, cancellationToken);
        if (vault is null)
        {
            return null;
        }

        await dbContext.Entry(vault)
            .Collection<TripQueryHistory>(QueryHistoryNavigationName)
            .Query()
            .Where(itemPredicate)
            .LoadAsync(cancellationToken);

        return vault;
    }

    private static int ClampUserTripsLimit(int limit) => Math.Clamp(limit, MinUserTripsLimit, MaxUserTripsLimit);

    private static string NormalizeNameForLookup(string value) => EscapeLikePattern(value.Trim());

    private static string EscapeLikePattern(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
