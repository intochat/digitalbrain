using AutoMapper;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Comms.Core.Extensions;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Infrastructure.Mappings;

public class LocalPlacesQueryProfile : Profile
{
    public LocalPlacesQueryProfile()
    {
        CreateMap<ScheduledLocalPlaceQuery, GetLocalPlacesRequestDTO>()
            .ForMember(dest => dest.SearchQuery,
                opt => opt.MapFrom(src => new SearchQuery { Q = src.SearchQuery }))
            .ForMember(dest => dest.GeographicLocationDto,
                opt => opt.MapFrom(src => src.AdditionalParameters.DeserializeAs<GeographicLocation>()))
            .ForMember(dest => dest.Filters,
                opt => opt.MapFrom(src => src.AdditionalParameters.DeserializeAs<LocalPlacesFiltersDTO>()))
            .ForMember(dest => dest.Pagination,
                opt => opt.MapFrom(src => src.AdditionalParameters.DeserializeAs<Pagination>()))
            .ForMember(dest => dest.Localization,
                opt => opt.MapFrom(src => src.AdditionalParameters.DeserializeAs<Localization>()));
    }
}
