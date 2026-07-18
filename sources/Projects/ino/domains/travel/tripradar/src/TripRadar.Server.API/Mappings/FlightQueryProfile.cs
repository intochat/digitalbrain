using AutoMapper;
using TripRadar.Server.API.Contracts.Enums;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Application.UseCases.SearchEngine.Flights.Commands.CreateScheduledFlightQuery;
using TripRadar.Server.Comms.Core.Extensions;
using AdvancedFilters = TripRadar.Server.API.Contracts.Models.AdvancedFilters;
using AdvancedSearchOptions = TripRadar.Server.API.Contracts.Models.AdvancedSearchOptions;
using Airport = TripRadar.Server.Application.DTO.Models.Airport;
using AirportDetail = TripRadar.Server.Application.DTO.Models.AirportDetail;
using AirportIdentifier = TripRadar.Server.Application.DTO.Models.AirportIdentifier;
using AirportInfo = TripRadar.Server.Application.DTO.Models.AirportInfo;
using BestFlightOption = TripRadar.Server.Application.DTO.Models.BestFlightOption;
using BookingFlights = TripRadar.Server.API.Contracts.Models.BookingFlights;
using CarbonEmissions = TripRadar.Server.API.Contracts.Models.CarbonEmissions;
using FlightOption = TripRadar.Server.API.Contracts.Models.FlightOption;
using FlightBookingOption = TripRadar.Server.API.Contracts.Models.FlightBookingOption;
using FlightBookingOptionDetail = TripRadar.Server.API.Contracts.Models.FlightBookingOptionDetail;
using FlightBookingRequest = TripRadar.Server.API.Contracts.Models.FlightBookingRequest;
using FlightPriceInsights = TripRadar.Server.API.Contracts.Models.FlightPriceInsights;
using FlightSearchParameters = TripRadar.Server.Application.DTO.Models.FlightSearchParameters;
using FlightSegment = TripRadar.Server.API.Contracts.Models.FlightSegment;
using FlightType = TripRadar.Server.API.Contracts.Enums.FlightType;
using Layover = TripRadar.Server.API.Contracts.Models.Layover;
using Localization = TripRadar.Server.API.Contracts.Models.Localization;
using MultiCityLeg = TripRadar.Server.API.Contracts.Models.MultiCityLeg;
using NextFlights = TripRadar.Server.API.Contracts.Models.NextFlights;
using PassengerInfo = TripRadar.Server.API.Contracts.Models.PassengerInfo;
using QueryColumn = TripRadar.Server.Domain.ValueObjects.QueryColumn;
using SearchMetadata = TripRadar.Server.Application.DTO.Models.SearchMetadata;
using SortingOptions = TripRadar.Server.API.Contracts.Models.SortingOptions;
using StopsType = TripRadar.Server.Application.DTO.Enums.StopsType;
using TravelClassType = TripRadar.Server.Application.DTO.Enums.TravelClassType;
using FlightBookingOptionDto = TripRadar.Server.Application.DTO.Models.FlightBookingOption;
using FlightBookingOptionDetailDto = TripRadar.Server.Application.DTO.Models.FlightBookingOptionDetail;
using FlightBookingRequestDto = TripRadar.Server.Application.DTO.Models.FlightBookingRequest;
using GetFlightPriceCalendarResponseDTO = TripRadar.Server.Application.DTO.Responses.GetFlightPriceCalendarResponseDTO;
using PriceCalendarDayDTO = TripRadar.Server.Application.DTO.Responses.PriceCalendarDayDTO;
using FlightExploreSearchMetadataDTO = TripRadar.Server.Application.DTO.Responses.FlightExploreSearchMetadataDTO;
using FlightExploreSearchParametersDTO = TripRadar.Server.Application.DTO.Responses.FlightExploreSearchParametersDTO;
using FlightExploreDestinationDTO = TripRadar.Server.Application.DTO.Responses.FlightExploreDestinationDTO;
using FlightExploreAirportDTO = TripRadar.Server.Application.DTO.Responses.FlightExploreAirportDTO;
using FlightExploreResultDTO = TripRadar.Server.Application.DTO.Responses.FlightExploreResultDTO;
using FlightExploreGpsCoordinatesDTO = TripRadar.Server.Application.DTO.Responses.FlightExploreGpsCoordinatesDTO;

namespace TripRadar.Server.API.Mappings;

internal sealed class FlightQueryProfile : Profile
{
    public FlightQueryProfile()
    {
        CreateMap<GetFlightRequest, GetFlightRequestDTO>();

        CreateMap<CreateScheduledFlightQueryRequest, CreateScheduledFlightQueryCommand>()
            .ConstructUsing(src => new CreateScheduledFlightQueryCommand(
                src.DepartureAirportCode,
                src.DestinationAirportCode,
                string.Empty,
                src.DepartureDate,
                src.ReturnDate,
                src.SelectedColumns != null
                    ? src.SelectedColumns.Select(i => new QueryColumn(i.Name, i.IsActive)).ToList()
                    : new List<QueryColumn>(),
                src.AdditionalParameters.SerializeParameters(),
                src.NextExecutionTime,
                src.Schedule
            ));

        CreateMap<FlightSearchQuery, FlightSearchQueryDTO>();
        CreateMap<Localization, Application.DTO.Models.Localization>();
        CreateMap<AdvancedSearchOptions, Application.DTO.Models.AdvancedSearchOptions>();
        CreateMap<PassengerInfo, Application.DTO.Models.PassengerInfo>();
        CreateMap<SortingOptions, Application.DTO.Models.SortingOptions>();
        CreateMap<AdvancedFilters, Application.DTO.Models.AdvancedFilters>();
        CreateMap<NextFlights, Application.DTO.Models.NextFlights>();
        CreateMap<BookingFlights, Application.DTO.Models.BookingFlights>();
        CreateMap<MultiCityLeg, Application.DTO.Models.MultiCityLeg>();

        CreateMap<FlightType, Application.DTO.Enums.FlightType>();
        CreateMap<Contracts.Enums.TravelClassType, TravelClassType>();
        CreateMap<FlightSortByType, SortBy>();
        CreateMap<Contracts.Enums.StopsType, StopsType>();

        CreateMap<GetFlightResponseDTO, GetFlightsResponse>();
        CreateMap<Application.DTO.Models.FlightOption, FlightOption>();
        CreateMap<Application.DTO.Models.FlightSegment, FlightSegment>();
        CreateMap<FlightBookingOptionDto, FlightBookingOption>();
        CreateMap<FlightBookingOptionDetailDto, FlightBookingOptionDetail>();
        CreateMap<FlightBookingRequestDto, FlightBookingRequest>();
        CreateMap<Airport, Contracts.Models.Airport>();
        CreateMap<Application.DTO.Models.CarbonEmissions, CarbonEmissions>();
        CreateMap<Application.DTO.Models.Layover, Layover>();
        CreateMap<Application.DTO.Models.FlightPriceInsights, FlightPriceInsights>();

        CreateMap<AirportInfo, Contracts.Models.AirportInfo>();
        CreateMap<AirportDetail, Contracts.Models.AirportDetail>();
        CreateMap<AirportIdentifier, Contracts.Models.AirportIdentifier>();
        CreateMap<BestFlightOption, Contracts.Models.BestFlightOption>();
        CreateMap<SearchMetadata, Contracts.Models.SearchMetadata>();
        CreateMap<FlightSearchParameters, Contracts.Models.FlightSearchParameters>();
        CreateMap<Contracts.Models.QueryColumn, QueryColumn>();

        // Flight Price Calendar mappings
        CreateMap<GetFlightPriceCalendarRequest, GetFlightPriceCalendarRequestDTO>();
        CreateMap<GetFlightPriceCalendarResponseDTO, GetFlightPriceCalendarResponse>();
        CreateMap<PriceCalendarDayDTO, PriceCalendarDay>();

        // Flight Explore mappings
        CreateMap<GetFlightExploreRequest, GetFlightExploreRequestDTO>();
        CreateMap<GetFlightExploreResponseDTO, GetFlightExploreResponse>();
        CreateMap<FlightExploreSearchMetadataDTO, Contracts.Models.SearchMetadata>();
        CreateMap<FlightExploreSearchParametersDTO, FlightExploreSearchParameters>();
        CreateMap<FlightExploreDestinationDTO, FlightExploreDestination>();
        CreateMap<FlightExploreAirportDTO, FlightExploreAirport>();
        CreateMap<FlightExploreResultDTO, FlightExploreResult>();
        CreateMap<FlightExploreGpsCoordinatesDTO, Contracts.Models.GpsCoordinates>();
    }
}
