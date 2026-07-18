using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Events.Queries.GetEvents;

public class GetEventsQueryHandler(ISerpApiQueryExecutor serpApiQueryExecutor) : IRequestHandler<GetEventsQuery, Result<GetEventResponseDTO>>
{
    public Task<Result<GetEventResponseDTO>> Handle(GetEventsQuery request, CancellationToken cancellationToken) =>
        serpApiQueryExecutor.ExecuteAsync<GetEventRequestDTO, GetEventResponseDTO>(request.GetEventRequestDto, ServiceType.Event, Errors.EventQueryDataNotFound, cancellationToken);
}
