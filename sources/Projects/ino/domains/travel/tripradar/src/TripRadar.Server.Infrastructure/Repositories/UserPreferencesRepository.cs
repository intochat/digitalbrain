using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public sealed class UserPreferencesRepository(TripRadarDbContext dbContext) : IUserPreferencesRepository
{
    public async Task<List<UserPreference>> GetByUserIdAsync(long userId, CancellationToken ct) =>
        await dbContext.UserPreferences
            .AsNoTracking()
            .Include(up => up.PreferenceType)
            .ThenInclude(pt => pt.ServiceType)
            .ThenInclude(serviceType => serviceType.PreferenceCategory)
            .Where(up => up.UserId == userId)
            .ToListAsync(ct);

    public async Task<List<UserPreference>> GetTrackedByUserIdAsync(long userId, CancellationToken ct) =>
        await dbContext.UserPreferences
            .Where(up => up.UserId == userId)
            .ToListAsync(ct);

    public async Task<List<UserPreference>> GetActiveByUserIdAndPreferenceTypeIdsAsync(
        long userId,
        IReadOnlyCollection<int> preferenceTypeIds,
        CancellationToken ct)
    {
        if (preferenceTypeIds.Count == 0)
        {
            return [];
        }

        return await dbContext.UserPreferences
            .AsNoTracking()
            .Where(up => up.UserId == userId && up.IsActive && preferenceTypeIds.Contains(up.PreferenceTypeId))
            .ToListAsync(ct);
    }

    public async Task AddRangeAsync(IEnumerable<UserPreference> preferences, CancellationToken ct) => await dbContext.UserPreferences.AddRangeAsync(preferences, ct);

    public void UpdateRange(IEnumerable<UserPreference> preferences) => dbContext.UserPreferences.UpdateRange(preferences);
}
