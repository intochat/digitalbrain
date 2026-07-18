using AutoMapper;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Responses.Create;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.API.Mappings;

internal sealed class InternalProfile : Profile
{
    public InternalProfile()
    {
        CreateMap<(decimal TokensDeducted, decimal RemainingTokens, decimal MonthlyLimit, bool LimitReached),
                DeductingTokensResponse>()
            .ForMember(dest => dest.TokensDeducted, opt => opt.MapFrom(src => src.TokensDeducted))
            .ForMember(dest => dest.RemainingTokens, opt => opt.MapFrom(src => src.RemainingTokens))
            .ForMember(dest => dest.MonthlyLimit, opt => opt.MapFrom(src => src.MonthlyLimit))
            .ForMember(dest => dest.LimitReached, opt => opt.MapFrom(src => src.LimitReached))
            .ForMember(dest => dest.CanUseApi, opt => opt.MapFrom(src => src.RemainingTokens > 0));

        CreateMap<TelegramAuthData, TelegramAuthDataDTO>();
    }
}
