using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IUserPreferencesRepository
{
    Task<List<UserPreference>> GetByUserIdAsync(long userId, CancellationToken ct = default);
    Task<List<UserPreference>> GetTrackedByUserIdAsync(long userId, CancellationToken ct = default);
    Task<List<UserPreference>> GetActiveByUserIdAndPreferenceTypeIdsAsync(long userId, IReadOnlyCollection<int> preferenceTypeIds, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<UserPreference> preferences, CancellationToken ct = default);
    void UpdateRange(IEnumerable<UserPreference> preferences);
}
