using AutoMapper;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.API.Mappings;

internal sealed class OverageProfile : Profile
{
    public OverageProfile()
    {
        CreateMap<GetOverageUsageDTO, OverageUsageResponse>();
    }
}
