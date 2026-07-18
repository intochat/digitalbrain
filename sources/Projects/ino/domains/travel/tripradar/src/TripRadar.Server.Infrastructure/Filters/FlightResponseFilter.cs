using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Constants.Flights;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.Infrastructure.Filters;

public class FlightResponseFilter(ILogger<FlightResponseFilter> logger)
    : BaseSearchResponseFilter<GetFlightResponseDTO>(logger)
{
    private static readonly Dictionary<string, string> _flightOptionMappings = new()
    {
        { nameof(FlightOption.Flights), SavedFlightQueryColumnNameConstants.Flights },
        { nameof(FlightOption.Layovers), SavedFlightQueryColumnNameConstants.Layovers },
        { nameof(FlightOption.TotalDuration), SavedFlightQueryColumnNameConstants.TotalDuration },
        { nameof(FlightOption.CarbonEmissions), SavedFlightQueryColumnNameConstants.CarbonEmissions },
        { nameof(FlightOption.Price), SavedFlightQueryColumnNameConstants.Price },
        { nameof(FlightOption.Type), SavedFlightQueryColumnNameConstants.Type },
        { nameof(FlightOption.AirlineLogo), SavedFlightQueryColumnNameConstants.AirlineLogo },
        { nameof(FlightOption.BookingToken), SavedFlightQueryColumnNameConstants.BookingToken }
    };

    private static readonly Dictionary<string, string> _flightSegmentMappings = new()
    {
        { nameof(FlightSegment.DepartureAirport), SavedFlightQueryColumnNameConstants.DepartureAirport },
        { nameof(FlightSegment.ArrivalAirport), SavedFlightQueryColumnNameConstants.ArrivalAirport },
        { nameof(FlightSegment.Duration), SavedFlightQueryColumnNameConstants.Duration },
        { nameof(FlightSegment.Airplane), SavedFlightQueryColumnNameConstants.Airplane },
        { nameof(FlightSegment.Airline), SavedFlightQueryColumnNameConstants.Airline },
        { nameof(FlightSegment.AirlineLogo), SavedFlightQueryColumnNameConstants.AirlineLogo },
        { nameof(FlightSegment.TravelClass), SavedFlightQueryColumnNameConstants.TravelClass },
        { nameof(FlightSegment.FlightNumber), SavedFlightQueryColumnNameConstants.FlightNumber },
        { nameof(FlightSegment.TicketAlsoSoldBy), SavedFlightQueryColumnNameConstants.TicketAlsoSoldBy },
        { nameof(FlightSegment.Legroom), SavedFlightQueryColumnNameConstants.Legroom },
        { nameof(FlightSegment.Extensions), SavedFlightQueryColumnNameConstants.Extensions },
        { nameof(FlightSegment.PlaneAndCrewBy), SavedFlightQueryColumnNameConstants.PlaneAndCrewBy }
    };

    private static readonly Dictionary<string, string> _layoverMappings = new()
    {
        { nameof(Layover.Duration), SavedFlightQueryColumnNameConstants.LayoverDuration },
        { nameof(Layover.Name), SavedFlightQueryColumnNameConstants.LayoverName },
        { nameof(Layover.Id), SavedFlightQueryColumnNameConstants.LayoverId }
    };

    private static readonly Dictionary<string, string> _priceInsightsMappings = new()
    {
        { nameof(FlightPriceInsights.LowestPrice), SavedFlightQueryColumnNameConstants.LowestPrice },
        { nameof(FlightPriceInsights.PriceLevel), SavedFlightQueryColumnNameConstants.PriceLevel },
        { nameof(FlightPriceInsights.TypicalPriceRange), SavedFlightQueryColumnNameConstants.TypicalPriceRange },
        { nameof(FlightPriceInsights.PriceHistory), SavedFlightQueryColumnNameConstants.PriceHistory }
    };

    private static readonly Dictionary<string, string> _airportInfoMappings = new()
    {
        { nameof(AirportInfo.Departure), SavedFlightQueryColumnNameConstants.Departure },
        { nameof(AirportInfo.Arrival), SavedFlightQueryColumnNameConstants.Arrival }
    };

    private static readonly Dictionary<string, string> _airportDetailMappings = new()
    {
        { nameof(AirportDetail.Airport), SavedFlightQueryColumnNameConstants.Airport },
        { nameof(AirportDetail.City), SavedFlightQueryColumnNameConstants.City },
        { nameof(AirportDetail.Country), SavedFlightQueryColumnNameConstants.Country },
        { nameof(AirportDetail.CountryCode), SavedFlightQueryColumnNameConstants.CountryCode },
        { nameof(AirportDetail.Image), SavedFlightQueryColumnNameConstants.Image },
        { nameof(AirportDetail.Thumbnail), SavedFlightQueryColumnNameConstants.Thumbnail }
    };

    protected override GetFlightResponseDTO FilterResponse(GetFlightResponseDTO response, List<string> activeColumns)
    {
        var filteredResponse = new GetFlightResponseDTO();

        if (ShouldIncludeContainer(SavedFlightQueryColumnNameConstants.SearchMetadata, activeColumns))
            filteredResponse.SearchMetadata = response.SearchMetadata;

        if (ShouldIncludeContainer(SavedFlightQueryColumnNameConstants.SearchParameters, activeColumns))
            filteredResponse.SearchParameters = response.SearchParameters;

        if (ShouldIncludeContainer(SavedFlightQueryColumnNameConstants.BestFlights, activeColumns) && response.BestFlights != null)
            filteredResponse.BestFlights = FilterFlightOptions(response.BestFlights, activeColumns);

        if (ShouldIncludeContainer(SavedFlightQueryColumnNameConstants.OtherFlights, activeColumns) && response.OtherFlights != null)
            filteredResponse.OtherFlights = FilterFlightOptions(response.OtherFlights, activeColumns);

        if (ShouldIncludeContainer(SavedFlightQueryColumnNameConstants.PriceInsights, activeColumns) && response.PriceInsights != null)
            filteredResponse.PriceInsights = FilterPriceInsights(response.PriceInsights, activeColumns);

        if (ShouldIncludeContainer(SavedFlightQueryColumnNameConstants.Airports, activeColumns) && response.Airports != null)
            filteredResponse.Airports = FilterAirports(response.Airports, activeColumns);

        return filteredResponse;
    }

    /// <summary>
    ///     Checks if a container should be included based on whether any of its child columns are active
    ///     or if the container itself is directly requested
    /// </summary>
    private static bool ShouldIncludeContainer(string containerName, List<string> activeColumns)
    {
        // Include if the container itself is directly requested
        if (IsColumnActive(containerName, activeColumns))
            return true;

        // Include if any child columns that belong to this container are requested
        return containerName switch
        {
            SavedFlightQueryColumnNameConstants.BestFlights => activeColumns.Any(IsFlightOptionColumn),
            SavedFlightQueryColumnNameConstants.OtherFlights => activeColumns.Any(IsFlightOptionColumn),
            SavedFlightQueryColumnNameConstants.SearchMetadata => activeColumns.Any(IsSearchMetadataColumn),
            SavedFlightQueryColumnNameConstants.SearchParameters => activeColumns.Any(IsSearchParametersColumn),
            SavedFlightQueryColumnNameConstants.PriceInsights => activeColumns.Any(IsPriceInsightsColumn),
            SavedFlightQueryColumnNameConstants.Airports => activeColumns.Any(IsAirportsColumn),
            _ => false
        };
    }

    private static bool IsFlightOptionColumn(string columnName)
    {
        return columnName is SavedFlightQueryColumnNameConstants.Flights
            or SavedFlightQueryColumnNameConstants.BestFlightsLayovers
            or SavedFlightQueryColumnNameConstants.TotalDuration
            or SavedFlightQueryColumnNameConstants.BestFlightsCarbonEmissions
            or SavedFlightQueryColumnNameConstants.Price or SavedFlightQueryColumnNameConstants.FlightType
            or SavedFlightQueryColumnNameConstants.AirlineLogo or SavedFlightQueryColumnNameConstants.BookingToken
            or SavedFlightQueryColumnNameConstants.DepartureAirport
            or SavedFlightQueryColumnNameConstants.ArrivalAirport or SavedFlightQueryColumnNameConstants.Duration
            or SavedFlightQueryColumnNameConstants.Airplane or SavedFlightQueryColumnNameConstants.Airline
            or SavedFlightQueryColumnNameConstants.FlightAirlineLogo or SavedFlightQueryColumnNameConstants.TravelClass
            or SavedFlightQueryColumnNameConstants.FlightNumber or SavedFlightQueryColumnNameConstants.TicketAlsoSoldBy
            or SavedFlightQueryColumnNameConstants.Legroom or SavedFlightQueryColumnNameConstants.Extensions
            or SavedFlightQueryColumnNameConstants.PlaneAndCrewBy or SavedFlightQueryColumnNameConstants.LayoverDuration
            or SavedFlightQueryColumnNameConstants.LayoverName or SavedFlightQueryColumnNameConstants.LayoverId;
    }

    private static bool IsSearchMetadataColumn(string columnName) =>
        columnName is SavedFlightQueryColumnNameConstants.CreatedAt or SavedFlightQueryColumnNameConstants.Status
            or SavedFlightQueryColumnNameConstants.ProcessedAt or SavedFlightQueryColumnNameConstants.TotalTimeTaken;

    private static bool IsSearchParametersColumn(string columnName) =>
        columnName is SavedFlightQueryColumnNameConstants.Engine
            or SavedFlightQueryColumnNameConstants.LanguageCode or SavedFlightQueryColumnNameConstants.CountryCode
            or SavedFlightQueryColumnNameConstants.Type or SavedFlightQueryColumnNameConstants.DepartureId
            or SavedFlightQueryColumnNameConstants.ArrivalId or SavedFlightQueryColumnNameConstants.OutboundDate
            or SavedFlightQueryColumnNameConstants.Adults or SavedFlightQueryColumnNameConstants.Currency;

    private static bool IsPriceInsightsColumn(string columnName) =>
        columnName is SavedFlightQueryColumnNameConstants.LowestPrice
            or SavedFlightQueryColumnNameConstants.PriceLevel or SavedFlightQueryColumnNameConstants.TypicalPriceRange
            or SavedFlightQueryColumnNameConstants.PriceHistory;

    private static bool IsAirportsColumn(string columnName) =>
        columnName is SavedFlightQueryColumnNameConstants.Airport
            or SavedFlightQueryColumnNameConstants.AirportName or SavedFlightQueryColumnNameConstants.AirportId
            or SavedFlightQueryColumnNameConstants.AirportTime or SavedFlightQueryColumnNameConstants.City
            or SavedFlightQueryColumnNameConstants.Country or SavedFlightQueryColumnNameConstants.Image
            or SavedFlightQueryColumnNameConstants.Thumbnail or SavedFlightQueryColumnNameConstants.Departure
            or SavedFlightQueryColumnNameConstants.Arrival;

    private static List<FlightOption> FilterFlightOptions(List<FlightOption> flightOptions, List<string> activeColumns) =>
        flightOptions.Select(option =>
        {
            var filteredOption = CreateFilteredInstance(option, activeColumns, _flightOptionMappings);

            if (IsColumnActive(SavedFlightQueryColumnNameConstants.Flights, activeColumns))
                filteredOption.Flights = FilterFlightSegments(option.Flights, activeColumns);

            if (IsColumnActive(SavedFlightQueryColumnNameConstants.Layovers, activeColumns))
                filteredOption.Layovers = FilterLayovers(option.Layovers, activeColumns);

            return filteredOption;
        }).ToList();

    private static List<FlightSegment>? FilterFlightSegments(List<FlightSegment>? segments, List<string> activeColumns) =>
        segments?.Select(segment => CreateFilteredInstance(segment, activeColumns, _flightSegmentMappings))
            .ToList();

    private static List<Layover>? FilterLayovers(List<Layover>? layovers, List<string> activeColumns) => layovers?.Select(layover => CreateFilteredInstance(layover, activeColumns, _layoverMappings)).ToList();

    private static FlightPriceInsights? FilterPriceInsights(FlightPriceInsights? insights, List<string> activeColumns) => insights == null ? null : CreateFilteredInstance(insights, activeColumns, _priceInsightsMappings);

    private static List<AirportInfo>? FilterAirports(List<AirportInfo>? airports, List<string> activeColumns)
    {
        return airports?.Select(airport =>
        {
            var filteredAirport = CreateFilteredInstance(airport, activeColumns, _airportInfoMappings);

            if (IsColumnActive(SavedFlightQueryColumnNameConstants.DepartureAirport, activeColumns))
                filteredAirport.Departure = FilterAirportDetails(airport.Departure, activeColumns);

            if (IsColumnActive(SavedFlightQueryColumnNameConstants.ArrivalAirport, activeColumns))
                filteredAirport.Arrival = FilterAirportDetails(airport.Arrival, activeColumns);

            return filteredAirport;
        }).ToList();
    }

    private static List<AirportDetail>? FilterAirportDetails(List<AirportDetail>? details, List<string> activeColumns) =>
        details?.Select(detail => CreateFilteredInstance(detail, activeColumns, _airportDetailMappings))
            .ToList();
}
