using AutoMapper;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.API.Mappings;

internal sealed class TimezoneProfile : Profile
{
    public TimezoneProfile()
    {
        CreateMap<TimezoneResponseDTO, TimezoneResponse>();
    }
}
