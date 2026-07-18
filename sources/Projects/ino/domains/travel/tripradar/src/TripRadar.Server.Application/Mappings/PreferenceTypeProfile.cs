using AutoMapper;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.Mappings;

public sealed class PreferenceTypeProfile : Profile
{
    public PreferenceTypeProfile()
    {
        CreateMap<PreferenceType, PreferenceTypeResponseDTO>()
            .ForMember(dest => dest.ServiceTypeName, opt => opt.MapFrom(src => src.ServiceType.Name))
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
            .ForMember(dest => dest.DataType, opt => opt.MapFrom(src => src.DataType))
            .ForMember(dest => dest.ValidationSchema, opt => opt.MapFrom(src => src.ValidationSchema))
            .ForMember(dest => dest.IsRequired, opt => opt.MapFrom(src => src.IsRequired))
            .ForMember(dest => dest.DefaultValue, opt => opt.MapFrom(src => src.DefaultValue));
    }
}
