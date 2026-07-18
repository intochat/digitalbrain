using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.Extensions;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Infrastructure.Contracts;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class UserRepository(TripRadarDbContext dbContext, IBlindIndexService blindIndexService)
    : Repository<User>(dbContext), IUserRepository
{
    private static readonly Func<TripRadarDbContext, long, IAsyncEnumerable<UserAuthSnapshot>> _getAuthSnapshotByIdQuery =
        EF.CompileAsyncQuery(
            (TripRadarDbContext context, long userId) =>
                context.Users
                    .AsNoTracking()
                    .Where(user => user.Id == userId)
                    .Select(user => new UserAuthSnapshot(user.IsActive, user.Profile.SecurityStamp)));

    public override async Task<User?> GetByIdAsync(object? id, CancellationToken cancellationToken = default) =>
        id is null ? null : await dbContext.Users.FindAsync([id], cancellationToken);

    public async Task CreateAsync(User user, CancellationToken cancellationToken = default)
    {
        user.UpdateTokenData(JwtExtensions.GenerateToken(), DateTime.UtcNow.AddDays(30));
        await dbContext.Users.AddAsync(user, cancellationToken);
    }

    public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default) =>
        BaseQuery().FirstOrDefaultAsync(user => user.Profile.UsernameHash == blindIndexService.ComputeHash(username), cancellationToken);

    public Task<User?> GetByUsernameWithProfileAndTierAsync(string username, CancellationToken cancellationToken = default) =>
        WithTierAndProfile(BaseQuery()).FirstOrDefaultAsync(user => user.Profile.UsernameHash == blindIndexService.ComputeHash(username), cancellationToken);

    public Task<User?> GetByUsernameWithSubscriptionAsync(string username, CancellationToken cancellationToken = default) =>
        WithSubscription(BaseQuery()).FirstOrDefaultAsync(user => user.Profile.UsernameHash == blindIndexService.ComputeHash(username), cancellationToken);

    public Task<User?> GetByUsernameReadOnlyAsync(string username, CancellationToken cancellationToken = default) =>
        WithSubscription(WithTierAndProfileLocalization(BaseQuery(asNoTracking: true))).FirstOrDefaultAsync(user => user.Profile.UsernameHash == blindIndexService.ComputeHash(username), cancellationToken);

    public Task<User?> GetByUsernameForLimitsAsync(string username, CancellationToken cancellationToken = default) =>
        WithSubscription(WithTierAndProfile(BaseQuery(asNoTracking: true)))
            .FirstOrDefaultAsync(user => user.Profile.UsernameHash == blindIndexService.ComputeHash(username), cancellationToken);

    public Task<User?> GetByIdForLimitsAsync(long userId, CancellationToken cancellationToken = default) =>
        WithSubscription(WithTierAndProfile(BaseQuery(asNoTracking: true)))
            .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public ValueTask<UserAuthSnapshot?> GetAuthSnapshotByIdAsync(long userId, CancellationToken cancellationToken = default) =>
        _getAuthSnapshotByIdQuery(dbContext, userId).FirstOrDefaultAsync(cancellationToken);

    public Task<User?> GetByIdWithProfileAsync(long userId, CancellationToken cancellationToken = default) =>
        WithSubscription(WithTierAndProfileLocalization(BaseQuery()))
            .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        WithTierAndProfileLocalization(BaseQuery())
            .FirstOrDefaultAsync(user => user.Profile.EmailHash == blindIndexService.ComputeHash(email), cancellationToken);

    public Task<User?> GetAuthByUsernameAsync(string username, CancellationToken cancellationToken = default) =>
        WithSubscription(WithTierAndProfileLocalization(BaseQuery()))
            .FirstOrDefaultAsync(user => user.Profile.UsernameHash == blindIndexService.ComputeHash(username), cancellationToken);

    public Task<User?> GetAuthByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        WithSubscription(WithTierAndProfileLocalization(BaseQuery()))
            .FirstOrDefaultAsync(user => user.Profile.EmailHash == blindIndexService.ComputeHash(email), cancellationToken);

    public Task<User?> GetAuthByTelegramUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
        WithSubscription(WithTierAndProfileLocalization(BaseQuery()))
            .FirstOrDefaultAsync(user => user.Profile.TelegramUserId == telegramUserId, cancellationToken);

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
        BaseQuery(asNoTracking: true)
            .AnyAsync(user => user.Profile.EmailHash == blindIndexService.ComputeHash(email), cancellationToken);

    public Task<User?> GetByGoogleIdAsync(string googleId, CancellationToken cancellationToken = default) =>
        WithTierAndProfileLocalization(BaseQuery(asNoTracking: true))
            .FirstOrDefaultAsync(user => user.Profile.GoogleId == googleId, cancellationToken);

    public async Task<List<long>> GetAllActiveUserIdsAsync(CancellationToken cancellationToken = default) =>
        await BaseQuery(asNoTracking: true)
            .Where(user => user.IsActive)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);

    public async Task<List<User>> GetUsersByIdsWithTierAsync(List<long> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        return await WithTierAndProfileLocalization(BaseQuery(asNoTracking: true))
            .Where(user => userIds.Contains(user.Id))
            .ToListAsync(cancellationToken);
    }

    private IQueryable<User> BaseQuery(bool asNoTracking = false)
    {
        var query = dbContext.Users.AsQueryable();
        return asNoTracking ? query.AsNoTracking() : query;
    }

    private static IQueryable<User> WithTierAndProfile(IQueryable<User> query) =>
        query
            .Include(user => user.Tier)
            .Include(user => user.Profile)
                .ThenInclude(profile => profile.TimezoneReference);

    private static IQueryable<User> WithTierAndProfileLocalization(IQueryable<User> query) =>
        query
            .Include(user => user.Tier)
            .Include(user => user.Profile)
                .ThenInclude(profile => profile.Language)
            .Include(user => user.Profile)
                .ThenInclude(profile => profile.Country)
            .Include(user => user.Profile)
                .ThenInclude(profile => profile.TimezoneReference);

    private static IQueryable<User> WithSubscription(IQueryable<User> query) =>
        query.Include(user => user.UserSubscription);
}
