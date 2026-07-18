using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.PlaceReviews.Queries.GetPlaceReviews;

public class GetPlaceReviewsQueryHandler(ISerpApiQueryExecutor serpApiQueryExecutor)
    : IRequestHandler<GetPlaceReviewsQuery, Result<GetPlaceReviewsResponseDTO>>
{
    public Task<Result<GetPlaceReviewsResponseDTO>> Handle(GetPlaceReviewsQuery request, CancellationToken ct) =>
        serpApiQueryExecutor.ExecuteAsync<GetPlaceReviewsRequestDTO, GetPlaceReviewsResponseDTO>(
            request.Request,
            ServiceType.PlaceReview,
            Errors.PlaceReviewsQueryDataNotFound,
            ct);
}
