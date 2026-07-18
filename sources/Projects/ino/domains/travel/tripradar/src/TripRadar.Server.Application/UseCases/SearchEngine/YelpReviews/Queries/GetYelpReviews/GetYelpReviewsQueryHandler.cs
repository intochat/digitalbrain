using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.YelpReviews.Queries.GetYelpReviews;

public class GetYelpReviewsQueryHandler(ISerpApiQueryExecutor serpApiQueryExecutor) : IRequestHandler<GetYelpReviewsQuery, Result<GetYelpReviewsResponseDTO>>
{
    public Task<Result<GetYelpReviewsResponseDTO>> Handle(GetYelpReviewsQuery request, CancellationToken cancellationToken) =>
        serpApiQueryExecutor.ExecuteAsync<GetYelpReviewsRequestDTO, GetYelpReviewsResponseDTO>(request.Request, ServiceType.YelpReviews, Errors.YelpReviewsDataNotFound, cancellationToken);
}
