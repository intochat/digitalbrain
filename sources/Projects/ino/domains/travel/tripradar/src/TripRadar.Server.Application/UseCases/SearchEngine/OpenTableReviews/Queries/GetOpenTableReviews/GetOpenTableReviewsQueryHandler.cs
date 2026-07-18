using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.OpenTableReviews.Queries.GetOpenTableReviews;

public class GetOpenTableReviewsQueryHandler(ISerpApiQueryExecutor serpApiQueryExecutor) : IRequestHandler<GetOpenTableReviewsQuery, Result<GetOpenTableReviewsResponseDTO>>
{
    public Task<Result<GetOpenTableReviewsResponseDTO>> Handle(GetOpenTableReviewsQuery request, CancellationToken cancellationToken) =>
        serpApiQueryExecutor.ExecuteAsync<GetOpenTableReviewsRequestDTO, GetOpenTableReviewsResponseDTO>(request.Request, ServiceType.OpenTableReview, Errors.OpenTableReviewDataNotFound, cancellationToken);
}
