using AutoMapper;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.Infrastructure.Models;

namespace TripRadar.Server.API.Mappings;

internal sealed class AuthProfile : Profile
{
    public AuthProfile()
    {
        CreateMap<CreateLoginRequest, TokenModel>();
    }
}
