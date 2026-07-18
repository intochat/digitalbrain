using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Hotels.Queries.GetHotels;

public class GetHotelsQueryHandler(ISerpApiQueryExecutor serpApiQueryExecutor) : IRequestHandler<GetHotelsQuery, Result<GetHotelResponseDTO>>
{
    public Task<Result<GetHotelResponseDTO>> Handle(GetHotelsQuery request, CancellationToken cancellationToken) =>
        serpApiQueryExecutor.ExecuteAsync<GetHotelRequestDTO, GetHotelResponseDTO>(request.GetHotelRequestDto, ServiceType.Hotel, Errors.HotelQueryDataNotFound, cancellationToken);
}
