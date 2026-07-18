using AutoMapper;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.API.Mappings;

internal sealed class MapsDirectionsQueryProfile : Profile
{
    public MapsDirectionsQueryProfile()
    {
        CreateMap<GetMapsDirectionsRequest, GetMapsDirectionsRequestDTO>();
        CreateMap<GetMapsPlaceResultsRequest, GetMapsPlaceResultsRequestDTO>();

        CreateMap<MapsDirectionsSearchParametersDTO, MapsDirectionsSearchParameters>();
        CreateMap<MapsPlaceResultsSearchParametersDTO, MapsPlaceResultsSearchParameters>();

        CreateMap<GetMapsDirectionsResponseDTO, GetMapsDirectionsResponse>();
        CreateMap<GetMapsPlaceResultsResponseDTO, GetMapsPlaceResultsResponse>();
    }
}
