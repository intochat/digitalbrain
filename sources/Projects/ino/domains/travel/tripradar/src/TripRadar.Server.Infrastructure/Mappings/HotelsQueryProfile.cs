using System.Globalization;
using AutoMapper;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Comms.Core.Extensions;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Infrastructure.Mappings;

public class HotelsQueryProfile : Profile
{
    public HotelsQueryProfile()
    {
        CreateMap<ScheduledHotelQuery, GetHotelRequestDTO>()
            .ForMember(dest => dest.SearchQuery,
                opt => opt.MapFrom(src => new SearchQuery { Q = src.Location }))
            .ForMember(dest => dest.AdvancedParameters,
                opt => opt.MapFrom(src => new HotelAdvancedParameters
                {
                    CheckInDate = src.CheckInDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    CheckOutDate = src.CheckOutDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                }))
            .ForMember(dest => dest.Filters,
                opt => opt.MapFrom(src => src.AdditionalParameters.DeserializeAs<HotelAdvancedFilters>()))
            .ForMember(dest => dest.VacationRentalsFilters,
                opt => opt.MapFrom(src => src.AdditionalParameters.DeserializeAs<VacationRentalsFilters>()));
    }
}
