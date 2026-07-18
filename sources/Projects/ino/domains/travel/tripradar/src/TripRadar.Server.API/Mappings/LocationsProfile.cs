using AutoMapper;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.API.Mappings;

internal sealed class LocationsProfile : Profile
{
    public LocationsProfile()
    {
        CreateMap<LocationSuggestionResponseDTO, LocationSuggestionItem>()
            .ForMember(dest => dest.Latitude, opt => opt.MapFrom(src => src.GpsLatitude))
            .ForMember(dest => dest.Longitude, opt => opt.MapFrom(src => src.GpsLongitude));

        CreateMap<IReadOnlyList<LocationSuggestionResponseDTO>, GetLocationSuggestionsResponse>()
            .ForMember(dest => dest.Locations, opt => opt.MapFrom(src => src));
    }
}
