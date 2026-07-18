using AutoMapper;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Comms.Core.Extensions;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Infrastructure.Mappings;

public class EventsQueryProfile : Profile
{
    public EventsQueryProfile()
    {
        CreateMap<ScheduledEventQuery, GetEventRequestDTO>()
            .ForMember(dest => dest.Username, opt => opt.MapFrom(src => src.User.Profile.Username))
            .ForMember(dest => dest.SearchQuery, opt => opt.MapFrom(src => new SearchQuery { Q = src.SearchQuery }))
            .ForMember(dest => dest.GeographicLocation,
                opt => opt.MapFrom(src => new GeographicLocation
                {
                    Location = src.AdditionalParameters.GetParameter<string>("location"),
                    Uule = src.AdditionalParameters.GetParameter<string>("uule")
                }))
            .ForMember(dest => dest.Localization,
                opt => opt.MapFrom(src => new Localization
                {
                    Hl = src.AdditionalParameters.GetParameter<string>("hl"),
                    Gl = src.AdditionalParameters.GetParameter<string>("gl")
                }))
            .ForMember(dest => dest.Filters,
                opt => opt.MapFrom(src =>
                    new EventFilters { Htichips = src.AdditionalParameters.GetParameter<List<string>>("htichips") }))
            .ForMember(dest => dest.NextPage, opt => opt.MapFrom(src =>
                src.AdditionalParameters.GetParameter<string>("start") != null
                    ? new PageToken { NextPageToken = src.AdditionalParameters.GetParameter<string>("start") }
                    : null));
    }
}
