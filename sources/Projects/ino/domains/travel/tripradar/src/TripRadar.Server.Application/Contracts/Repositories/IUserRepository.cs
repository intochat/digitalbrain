using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IUserRepository : IRepository<User>
{
    Task CreateAsync(User user, CancellationToken cancellationToken = default);

    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task<User?> GetByUsernameWithProfileAndTierAsync(string username, CancellationToken cancellationToken = default);

    Task<User?> GetByUsernameWithSubscriptionAsync(string username, CancellationToken cancellationToken = default);

    Task<User?> GetByUsernameReadOnlyAsync(string username, CancellationToken cancellationToken = default);

    Task<User?> GetByUsernameForLimitsAsync(string username, CancellationToken cancellationToken = default);

    Task<User?> GetByIdForLimitsAsync(long userId, CancellationToken cancellationToken = default);

    ValueTask<UserAuthSnapshot?> GetAuthSnapshotByIdAsync(long userId, CancellationToken cancellationToken = default);

    Task<User?> GetByIdWithProfileAsync(long userId, CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<User?> GetAuthByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task<User?> GetAuthByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<User?> GetAuthByTelegramUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);

    Task<User?> GetByGoogleIdAsync(string googleId, CancellationToken cancellationToken = default);

    Task<List<long>> GetAllActiveUserIdsAsync(CancellationToken cancellationToken = default);

    Task<List<User>> GetUsersByIdsWithTierAsync(List<long> userIds, CancellationToken cancellationToken = default);
}
