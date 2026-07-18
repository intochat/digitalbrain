using AutoMapper;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.API.Mappings;

internal sealed class AirportsProfile : Profile
{
    public AirportsProfile()
    {
        CreateMap<AirportSuggestionResponseDTO, AirportSuggestionItem>();

        CreateMap<IReadOnlyList<AirportSuggestionResponseDTO>, GetAirportSuggestionsResponse>()
            .ForMember(dest => dest.Airports, opt => opt.MapFrom(src => src));
    }
}
