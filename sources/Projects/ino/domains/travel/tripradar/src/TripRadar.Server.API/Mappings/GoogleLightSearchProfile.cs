using AutoMapper;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using ApiGeographicLocation = TripRadar.Server.API.Contracts.Models.GeographicLocation;
using Localization = TripRadar.Server.API.Contracts.Models.Localization;
using Pagination = TripRadar.Server.Application.DTO.Models.Pagination;
using SearchQuery = TripRadar.Server.Application.DTO.Models.SearchQuery;
using GeographicLocation = TripRadar.Server.Application.DTO.Models.GeographicLocation;

namespace TripRadar.Server.API.Mappings;

internal sealed class GoogleLightSearchProfile : Profile
{
    public GoogleLightSearchProfile()
    {
        CreateMap<GetGoogleLightSearchRequest, GetGoogleLightSearchRequestDTO>();
        CreateMap<Contracts.Models.SearchQuery, SearchQuery>();
        CreateMap<ApiGeographicLocation, GeographicLocation>();
        CreateMap<Contracts.Models.Pagination, Pagination>();
        CreateMap<Localization, Application.DTO.Models.Localization>()
            .ForMember(dest => dest.Domain, opt => opt.MapFrom(src => src.GoogleDomain));
        CreateMap<GoogleLightSearchParametersDTO, GoogleLightSearchParameters>();
        CreateMap<GetGoogleLightSearchResponseDTO, GetGoogleLightSearchResponse>();
    }
}
