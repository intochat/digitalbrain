using MediatR;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.UseCases.Feedbacks.Queries.GetAllFeedbacks;

public class GetAllFeedbacksQueryHandler(IFeedbackRepository feedbackRepository) : IRequestHandler<GetAllFeedbacksQuery, Result<PaginatedResultDTO<Feedback>>>
{
    public async Task<Result<PaginatedResultDTO<Feedback>>> Handle(GetAllFeedbacksQuery request, CancellationToken cancellationToken)
    {
        var feedbacks = await feedbackRepository.GetFeedbacksPagedAsync(request.PageNumber, request.PageSize, cancellationToken);
        var totalCount = await feedbackRepository.GetFeedbacksCountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize);
        return Result.Success(new PaginatedResultDTO<Feedback>(feedbacks, totalCount, request.PageNumber, request.PageSize, totalPages));
    }
}
