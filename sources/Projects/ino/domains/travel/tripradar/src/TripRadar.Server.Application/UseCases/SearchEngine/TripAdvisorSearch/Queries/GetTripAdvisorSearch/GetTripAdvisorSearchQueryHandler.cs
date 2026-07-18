using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.TripAdvisorSearch.Queries.GetTripAdvisorSearch;

public class GetTripAdvisorSearchQueryHandler(ISerpApiQueryExecutor serpApiQueryExecutor) : IRequestHandler<GetTripAdvisorSearchQuery, Result<GetTripAdvisorSearchResponseDTO>>
{
    public Task<Result<GetTripAdvisorSearchResponseDTO>> Handle(GetTripAdvisorSearchQuery request, CancellationToken ct) =>
        serpApiQueryExecutor.ExecuteAsync<GetTripAdvisorSearchRequestDTO, GetTripAdvisorSearchResponseDTO>(request.Request, ServiceType.TripAdvisorSearch, Errors.TripAdvisorSearchDataNotFound, ct);
}
