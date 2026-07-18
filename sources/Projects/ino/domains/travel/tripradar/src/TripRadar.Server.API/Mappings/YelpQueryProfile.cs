using AutoMapper;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.API.Mappings;

internal sealed class YelpQueryProfile : Profile
{
    public YelpQueryProfile()
    {
        CreateMap<GetYelpSearchRequest, GetYelpSearchRequestDTO>();
        CreateMap<GetYelpPlaceRequest, GetYelpPlaceRequestDTO>();
        CreateMap<GetYelpPlaceFullMenuRequest, GetYelpPlaceFullMenuRequestDTO>();
        CreateMap<GetYelpReviewsRequest, GetYelpReviewsRequestDTO>();

        CreateMap<YelpSearchParametersDTO, YelpSearchParameters>();
        CreateMap<YelpPlaceSearchParametersDTO, YelpPlaceSearchParameters>();
        CreateMap<YelpReviewsSearchParametersDTO, YelpReviewsSearchParameters>();

        CreateMap<GetYelpSearchResponseDTO, GetYelpSearchResponse>();
        CreateMap<GetYelpPlaceResponseDTO, GetYelpPlaceResponse>();
        CreateMap<GetYelpPlaceFullMenuResponseDTO, GetYelpPlaceFullMenuResponse>();
        CreateMap<GetYelpReviewsResponseDTO, GetYelpReviewsResponse>();
    }
}
