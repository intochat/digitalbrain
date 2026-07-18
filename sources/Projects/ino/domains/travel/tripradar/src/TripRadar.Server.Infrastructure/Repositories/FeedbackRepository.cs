using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.ReferenceData;
using TripRadar.Server.Infrastructure.Contracts;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class FeedbackRepository(TripRadarDbContext context, IBlindIndexService blindIndexService)
    : Repository<Feedback>(context), IFeedbackRepository
{
    public async Task<IEnumerable<Feedback>> GetUserFeedbacksAsync(string username, CancellationToken cancellationToken) =>
        await context.Feedbacks
            .AsNoTracking()
            .Include(f => f.User)
                .ThenInclude(u => u.Profile)
            .Include(f => f.Category)
            .Where(f => f.User.Profile.UsernameHash == blindIndexService.ComputeHash(username))
            .OrderByDescending(f => f.CreatedOn)
            .ToListAsync(cancellationToken);

    public async Task<IEnumerable<Feedback>> GetFeedbacksPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken) =>
        await context.Feedbacks
            .AsNoTracking()
            .Include(f => f.User)
                .ThenInclude(u => u.Profile)
            .Include(f => f.Category)
            .OrderByDescending(f => f.CreatedOn)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

    public async Task<int> GetFeedbacksCountAsync(CancellationToken cancellationToken) =>
       await context.Feedbacks.AsNoTracking().CountAsync(cancellationToken);

    public async Task<IEnumerable<FeedbackCategory>> GetFeedbackCategoriesAsync(CancellationToken cancellationToken) =>
        await context.FeedbackCategories.AsNoTracking().ToListAsync(cancellationToken);

    public Task<int> CountUserFeedbackSinceAsync(long userId, DateTime since, CancellationToken cancellationToken) =>
        context.Feedbacks
            .AsNoTracking()
            .Where(f => f.UserId == userId && f.CreatedOn >= since)
            .CountAsync(cancellationToken);
}
