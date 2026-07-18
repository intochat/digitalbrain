using AutoMapper;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Application.UseCases.SearchEngine.Hotels.Commands.CreateScheduledHotelQuery;
using TripRadar.Server.Comms.Core.Extensions;
using Brand = TripRadar.Server.API.Contracts.Models.Brand;
using BrandChild = TripRadar.Server.API.Contracts.Models.BrandChild;
using GpsCoordinates = TripRadar.Server.API.Contracts.Models.GpsCoordinates;
using HotelAdvancedFilters = TripRadar.Server.API.Contracts.Models.HotelAdvancedFilters;
using HotelAdvancedParameters = TripRadar.Server.API.Contracts.Models.HotelAdvancedParameters;
using HotelBooking = TripRadar.Server.API.Contracts.Models.HotelBooking;
using HotelSearchParameters = TripRadar.Server.API.Contracts.Models.HotelSearchParameters;
using Image = TripRadar.Server.API.Contracts.Models.Image;
using Localization = TripRadar.Server.API.Contracts.Models.Localization;
using NearbyPlace = TripRadar.Server.API.Contracts.Models.NearbyPlace;
using Pagination = TripRadar.Server.Application.DTO.Models.Pagination;
using Price = TripRadar.Server.API.Contracts.Models.Price;
using Property = TripRadar.Server.API.Contracts.Models.Property;
using QueryColumn = TripRadar.Server.Domain.ValueObjects.QueryColumn;
using Rate = TripRadar.Server.API.Contracts.Models.Rate;
using Rating = TripRadar.Server.API.Contracts.Models.Rating;
using ReviewBreakdown = TripRadar.Server.API.Contracts.Models.ReviewBreakdown;
using SearchInformation = TripRadar.Server.Application.DTO.Models.SearchInformation;
using SearchMetadata = TripRadar.Server.API.Contracts.Models.SearchMetadata;
using SearchQuery = TripRadar.Server.Application.DTO.Models.SearchQuery;
using SerpapiPagination = TripRadar.Server.API.Contracts.Models.SerpapiPagination;
using Transportation = TripRadar.Server.API.Contracts.Models.Transportation;
using VacationRentalsFilters = TripRadar.Server.API.Contracts.Models.VacationRentalsFilters;

namespace TripRadar.Server.API.Mappings;

internal sealed class HotelQueryProfile : Profile
{
    public HotelQueryProfile()
    {
        CreateMap<GetHotelRequest, GetHotelRequestDTO>()
            .ForMember(dest => dest.SearchQuery, opt => opt.MapFrom(src => src.SearchQuery))
            .ForMember(dest => dest.AdvancedParameters, opt => opt.MapFrom(src => src.AdvancedParameters))
            .ForMember(dest => dest.Localization, opt => opt.MapFrom(src => src.Localization))
            .ForMember(dest => dest.Filters, opt => opt.MapFrom(src => src.Filters))
            .ForMember(dest => dest.VacationRentalsFilters, opt => opt.MapFrom(src => src.VacationRentalsFilters))
            .ForMember(dest => dest.NextPage, opt => opt.MapFrom(src => src.TokenPagination))
            .ForMember(dest => dest.Booking, opt => opt.MapFrom(src => src.Booking));

        CreateMap<CreateScheduledHotelQueryRequest, CreateScheduledHotelQueryCommand>()
            .ConstructUsing(src => new CreateScheduledHotelQueryCommand(
                src.Location,
                string.Empty,
                src.CheckInDate,
                src.CheckOutDate,
                src.SelectedColumns != null
                    ? src.SelectedColumns.Select(i => new QueryColumn(i.Name, i.IsActive)).ToList()
                    : new List<QueryColumn>(),
                src.AdditionalParameters.SerializeParameters(),
                src.NextExecutionTime,
                src.Schedule
            ));

        CreateMap<Contracts.Models.SearchQuery, SearchQuery>();
        CreateMap<HotelAdvancedParameters, Application.DTO.Models.HotelAdvancedParameters>()
            .ForMember(dest => dest.ChildrenAges, opt => opt.MapFrom(src =>
                src.ChildrenAges != null ? string.Join(",", src.ChildrenAges) : null));
        CreateMap<Localization, Application.DTO.Models.Localization>();
        CreateMap<HotelAdvancedFilters, Application.DTO.Models.HotelAdvancedFilters>();
        CreateMap<VacationRentalsFilters, Application.DTO.Models.VacationRentalsFilters>();
        CreateMap<TokenPagination, Pagination>();
        CreateMap<HotelBooking, Application.DTO.Models.HotelBooking>();

        CreateMap<GetHotelResponseDTO, GetHotelsResponse>();
        CreateMap<Application.DTO.Models.SearchMetadata, SearchMetadata>();
        CreateMap<Application.DTO.Models.HotelSearchParameters, HotelSearchParameters>();
        CreateMap<SearchInformation, Contracts.Models.SearchInformation>();
        CreateMap<Application.DTO.Models.Brand, Brand>();
        CreateMap<Application.DTO.Models.BrandChild, BrandChild>();
        CreateMap<Application.DTO.Models.Property, Property>();
        CreateMap<Application.DTO.Models.GpsCoordinates, GpsCoordinates>();
        CreateMap<Application.DTO.Models.Rate, Rate>();
        CreateMap<Application.DTO.Models.Price, Price>();
        CreateMap<Application.DTO.Models.NearbyPlace, NearbyPlace>();
        CreateMap<Application.DTO.Models.Transportation, Transportation>();
        CreateMap<Application.DTO.Models.Image, Image>();
        CreateMap<Application.DTO.Models.Rating, Rating>();
        CreateMap<Application.DTO.Models.ReviewBreakdown, ReviewBreakdown>();
        CreateMap<Application.DTO.Models.SerpapiPagination, SerpapiPagination>();

        CreateMap<Contracts.Models.QueryColumn, QueryColumn>();
    }
}
