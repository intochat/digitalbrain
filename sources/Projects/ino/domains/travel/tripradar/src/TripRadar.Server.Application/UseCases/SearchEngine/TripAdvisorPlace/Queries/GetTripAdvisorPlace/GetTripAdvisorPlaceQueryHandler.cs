using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.TripAdvisorPlace.Queries.GetTripAdvisorPlace;

public class GetTripAdvisorPlaceQueryHandler(ISerpApiQueryExecutor serpApiQueryExecutor) : IRequestHandler<GetTripAdvisorPlaceQuery, Result<GetTripAdvisorPlaceResponseDTO>>
{
    public Task<Result<GetTripAdvisorPlaceResponseDTO>> Handle(GetTripAdvisorPlaceQuery request, CancellationToken cancellationToken) =>
        serpApiQueryExecutor.ExecuteAsync<GetTripAdvisorPlaceRequestDTO, GetTripAdvisorPlaceResponseDTO>(request.Request, ServiceType.TripAdvisorPlace, Errors.TripAdvisorPlaceDataNotFound, cancellationToken);
}
