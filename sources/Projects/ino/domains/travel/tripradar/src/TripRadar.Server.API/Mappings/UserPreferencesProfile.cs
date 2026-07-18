using AutoMapper;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.API.Mappings;

internal sealed class UserPreferencesProfile : Profile
{
    public UserPreferencesProfile()
    {
        CreateMap<UserPreferencesResponseDTO, GetUserPreferencesResponse>()
            .ForMember(dest => dest.Preferences, opt => opt.MapFrom(src => src.Preferences));

        CreateMap<UserPreferenceDTO, UserPreference>();

        CreateMap<PreferenceTypeResponseDTO, PreferenceType>()
            .ForMember(dest => dest.ServiceTypeName, opt => opt.MapFrom(src => src.ServiceTypeName))
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
            .ForMember(dest => dest.DataType, opt => opt.MapFrom(src => src.DataType))
            .ForMember(dest => dest.ValidationSchema, opt => opt.MapFrom(src => src.ValidationSchema))
            .ForMember(dest => dest.IsRequired, opt => opt.MapFrom(src => src.IsRequired))
            .ForMember(dest => dest.DefaultValue, opt => opt.MapFrom(src => src.DefaultValue));

        CreateMap<PreferenceCategoriesResponseDTO, GetPreferenceCategoriesResponse>();
        CreateMap<PreferenceCategoryDTO, PreferenceCategory>();
        CreateMap<PreferenceServiceDTO, PreferenceService>();

        CreateMap<UserPreferences, UserPreferencePatchRequestDTO>();
        CreateMap<FlightPreferences, FlightPreferencesDTO>();
        CreateMap<HotelPreferences, HotelPreferencesDTO>();
        CreateMap<EventPreferences, EventPreferencesDTO>();
        CreateMap<LocalPlacesPreferences, LocalPlacesPreferencesDTO>();
        CreateMap<MapsPreferences, MapsPreferencesDTO>();
        CreateMap<PlaceReviewPreferences, PlaceReviewPreferencesDTO>();
        CreateMap<TripAdvisorSearchPreferences, TripAdvisorSearchPreferencesDTO>();
        CreateMap<ServiceInfoDTO, ServiceInfo>();
    }
}
