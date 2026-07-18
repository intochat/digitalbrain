using AutoMapper;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.API.Mappings;

internal sealed class AirlineProfile : Profile
{
    public AirlineProfile()
    {
        CreateMap<AirlineResponseDTO, AirlineResponse>();
    }
}
