using AutoMapper;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.API.Contracts.Requests.Update;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.UseCases.PromoCodes.Commands.CreatePromoCode;
using TripRadar.Server.Application.UseCases.PromoCodes.Commands.UpdatePromoCode;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using DomainDiscountType = TripRadar.Server.Domain.Enums.DiscountType;

namespace TripRadar.Server.API.Mappings;

public sealed class PromoCodeProfile : Profile
{
    public PromoCodeProfile()
    {
        CreateMap<CreatePromoCodeRequest, CreatePromoCodeCommand>()
            .ForCtorParam("DiscountType", opt => opt.MapFrom(src =>
                (int)src.DiscountType == 1 ? DomainDiscountType.Percentage :
                (int)src.DiscountType == 2 ? DomainDiscountType.FixedAmount :
                new DomainDiscountType((int)src.DiscountType, "Invalid")));

        CreateMap<(string Code, UpdatePromoCodeRequest Request), UpdatePromoCodeCommand>()
            .ConstructUsing(src => new UpdatePromoCodeCommand(
                src.Code,
                src.Request.Description,
                src.Request.MaxUsageCount,
                src.Request.MaxUsagePerUser,
                src.Request.StartDate,
                src.Request.EndDate,
                src.Request.IsActive
            ));

        CreateMap<PromoCode, GetPromoCodeResponse>()
            .ForMember(dest => dest.Code, opt => opt.MapFrom(src => src.Code))
            .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description))
            .ForMember(dest => dest.DiscountType, opt => opt.MapFrom(src => (API.Contracts.Enums.DiscountType)src.DiscountTypeId))
            .ForMember(dest => dest.DiscountValue, opt => opt.MapFrom(src => src.DiscountValue))
            .ForMember(dest => dest.MaxUsageCount, opt => opt.MapFrom(src => src.MaxUsageCount))
            .ForMember(dest => dest.CurrentUsageCount, opt => opt.MapFrom(src => src.CurrentUsageCount))
            .ForMember(dest => dest.MaxUsagePerUser, opt => opt.MapFrom(src => src.MaxUsagePerUser))
            .ForMember(dest => dest.StartDate, opt => opt.MapFrom(src => src.StartDate))
            .ForMember(dest => dest.EndDate, opt => opt.MapFrom(src => src.EndDate))
            .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => src.IsActive))
            .ForMember(dest => dest.IsExpired, opt => opt.MapFrom(src => src.IsExpired()))
            .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.CreatedAt))
            .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => src.UpdatedAt));

        CreateMap<PromoCodeUsage, GetPromoCodeUsageResponse>()
            .ForMember(dest => dest.PromoCode, opt => opt.MapFrom(src => src.PromoCode != null ? src.PromoCode.Code : "N/A"))
            .ForMember(dest => dest.Username, opt => opt.MapFrom(src => src.User != null && src.User.Profile != null ? src.User.Profile.Username : null))
            .ForMember(dest => dest.UsedAt, opt => opt.MapFrom(src => src.UsedAt))
            .ForMember(dest => dest.DiscountApplied, opt => opt.MapFrom(src => src.DiscountApplied));
    }
}
