using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.YelpPlaceFullMenu.Queries.GetYelpPlaceFullMenu;

public class GetYelpPlaceFullMenuQueryHandler(ISerpApiQueryExecutor serpApiQueryExecutor)
    : IRequestHandler<GetYelpPlaceFullMenuQuery, Result<GetYelpPlaceFullMenuResponseDTO>>
{
    public Task<Result<GetYelpPlaceFullMenuResponseDTO>> Handle(GetYelpPlaceFullMenuQuery request, CancellationToken cancellationToken) =>
        serpApiQueryExecutor.ExecuteAsync<GetYelpPlaceFullMenuRequestDTO, GetYelpPlaceFullMenuResponseDTO>(request.Request, ServiceType.YelpPlaceFullMenu, Errors.YelpPlaceFullMenuDataNotFound, cancellationToken);
}
