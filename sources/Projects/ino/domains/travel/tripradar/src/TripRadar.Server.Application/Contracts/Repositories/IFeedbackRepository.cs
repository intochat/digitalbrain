using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IFeedbackRepository : IRepository<Feedback>
{
    Task<IEnumerable<Feedback>> GetUserFeedbacksAsync(string username, CancellationToken cancellationToken);

    Task<IEnumerable<Feedback>> GetFeedbacksPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken);

    Task<int> GetFeedbacksCountAsync(CancellationToken cancellationToken);

    Task<IEnumerable<FeedbackCategory>> GetFeedbackCategoriesAsync(CancellationToken cancellationToken);

    Task<int> CountUserFeedbackSinceAsync(long userId, DateTime since, CancellationToken cancellationToken);
}
