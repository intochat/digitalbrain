using AutoMapper;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.API.Mappings;

internal class TierProfile : Profile
{
    public TierProfile()
    {
        CreateMap<GetUserTierUsageResponseDTO, GetUserTierUsageResponse>();
    }
}
