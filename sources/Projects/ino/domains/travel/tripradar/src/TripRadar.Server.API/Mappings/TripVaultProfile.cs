using AutoMapper;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.API.Contracts.Requests.Update;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.Contracts.Repositories.Models;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using ApiServiceType = TripRadar.Server.API.Contracts.Enums.ServiceType;

namespace TripRadar.Server.API.Mappings;

internal sealed class TripVaultProfile : Profile
{
    public TripVaultProfile()
    {
        CreateMap<TripVault, TripVaultResponse>()
            .ForMember(dest => dest.ItemsCount, opt => opt.Ignore());

        CreateMap<TripVault, TripVaultDetailsResponse>()
            .ForMember(dest => dest.Items, opt => opt.MapFrom(src => src.QueryHistory));

        CreateMap<TripQueryHistory, TripItemResponse>()
            .ForMember(dest => dest.ServiceType, opt => opt.MapFrom(src => (ApiServiceType)src.ServiceTypeId));

        CreateMap<RecentSearchItemDetails, RecentSearchItemResponse>()
            .ForMember(dest => dest.ServiceType, opt => opt.MapFrom(src => (ApiServiceType)src.ServiceType.Id));

        CreateMap<RecentSearchPayloadDetails, RecentSearchPayloadResponse>()
            .Include<FlightRecentSearchPayloadDetails, FlightRecentSearchPayloadResponse>()
            .Include<HotelRecentSearchPayloadDetails, HotelRecentSearchPayloadResponse>();

        CreateMap<FlightRecentSearchPayloadDetails, FlightRecentSearchPayloadResponse>();
        CreateMap<HotelRecentSearchPayloadDetails, HotelRecentSearchPayloadResponse>();

        CreateMap<CreateTripVaultRequest, TripVaultResponse>()
            .ForMember(dest => dest.UniqueId, opt => opt.Ignore())
            .ForMember(dest => dest.ItemsCount, opt => opt.MapFrom(_ => 0))
            .ForMember(dest => dest.CreatedOn, opt => opt.MapFrom(_ => DateTime.UtcNow));

        CreateMap<UpdateTripVaultRequest, TripVaultResponse>()
            .ForMember(dest => dest.UniqueId, opt => opt.Ignore())
            .ForMember(dest => dest.ItemsCount, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedOn, opt => opt.MapFrom(_ => DateTime.UtcNow));
    }
}
