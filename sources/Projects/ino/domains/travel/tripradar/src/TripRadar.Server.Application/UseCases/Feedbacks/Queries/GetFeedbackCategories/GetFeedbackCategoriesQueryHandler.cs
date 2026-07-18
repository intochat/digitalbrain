using MediatR;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Application.UseCases.Feedbacks.Queries.GetFeedbackCategories;

public class GetFeedbackCategoriesQueryHandler(IFeedbackRepository feedbackRepository) : IRequestHandler<GetFeedbackCategoriesQuery, Result<IEnumerable<FeedbackCategory>>>
{
    public async Task<Result<IEnumerable<FeedbackCategory>>> Handle(GetFeedbackCategoriesQuery request, CancellationToken cancellationToken) =>
        Result.Success(await feedbackRepository.GetFeedbackCategoriesAsync(cancellationToken));
}
