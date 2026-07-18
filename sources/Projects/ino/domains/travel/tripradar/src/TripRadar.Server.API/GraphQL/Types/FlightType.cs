using System.Globalization;
using System.Text.Json;
using TripRadar.Server.API.Contracts.Enums;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Get;
using TripRadar.Server.API.Contracts.Responses.Get;
using AirportDetail = TripRadar.Server.API.Contracts.Models.AirportDetail;
using AirportInfo = TripRadar.Server.API.Contracts.Models.AirportInfo;
using FlightBookingOption = TripRadar.Server.API.Contracts.Models.FlightBookingOption;
using FlightBookingOptionDetail = TripRadar.Server.API.Contracts.Models.FlightBookingOptionDetail;
using FlightBookingRequest = TripRadar.Server.API.Contracts.Models.FlightBookingRequest;
using FlightOption = TripRadar.Server.API.Contracts.Models.FlightOption;
using FlightSegment = TripRadar.Server.API.Contracts.Models.FlightSegment;
using Layover = TripRadar.Server.API.Contracts.Models.Layover;
using SearchMetadata = TripRadar.Server.API.Contracts.Models.SearchMetadata;

namespace TripRadar.Server.API.GraphQL.Types;

public class FlightType : ObjectType<GetFlightsResponse>
{
    protected override void Configure(IObjectTypeDescriptor<GetFlightsResponse> descriptor)
    {
        descriptor.Description(
            "Represents a complete flight search response with metadata, search parameters, and flight options");

        descriptor.Field(f => f.SearchMetadata)
            .Type<FlightSearchMetadataType>()
            .Description("Metadata about the search request such as status and processing times");

        descriptor.Field(f => f.SearchParameters)
            .Type<FlightSearchParametersType>()
            .Description("Parameters used for the flight search");

        descriptor.Field(f => f.BestFlights)
            .Type<ListType<FlightOptionType>>()
            .Description("List of recommended flight options based on price and convenience");

        descriptor.Field(f => f.OtherFlights)
            .Type<ListType<FlightOptionType>>()
            .Description("Additional flight options beyond the best recommendations");

        descriptor.Field(f => f.PriceInsights)
            .Type<FlightPriceInsightsType>()
            .Description("Price analytics and historical data for the requested route");

        descriptor.Field(f => f.Airports)
            .Type<ListType<AirportInfoType>>()
            .Description("Information about the departure and arrival airports");

        descriptor.Field(f => f.BookingOptions)
            .Type<ListType<FlightBookingOptionType>>()
            .Description("Booking options for the selected flight");
    }
}

public class AirportDetailType : ObjectType<AirportDetail>
{
    protected override void Configure(IObjectTypeDescriptor<AirportDetail> descriptor)
    {
        descriptor.Description("Detailed information about an airport including location and imagery");

        descriptor.Field(a => a.Airport)
            .Type<AirportIdentifierType>()
            .Description("Airport identifier including ID and name");

        descriptor.Field(a => a.City)
            .Type<StringType>()
            .Description("City where the airport is located");

        descriptor.Field(a => a.Country)
            .Type<StringType>()
            .Description("Country where the airport is located");

        descriptor.Field(a => a.CountryCode)
            .Type<StringType>()
            .Description("Country code for the airport location");

        descriptor.Field(a => a.Image)
            .Type<StringType>()
            .Description("Full-size image URL for the airport or destination");

        descriptor.Field(a => a.Thumbnail)
            .Type<StringType>()
            .Description("Thumbnail image URL for the airport or destination");
    }
}

public class AirportIdentifierType : ObjectType<AirportIdentifier>
{
    protected override void Configure(IObjectTypeDescriptor<AirportIdentifier> descriptor)
    {
        descriptor.Description("Basic identifier information for an airport");

        descriptor.Field(a => a.Id)
            .Type<StringType>()
            .Description("Unique identifier for the airport");

        descriptor.Field(a => a.Name)
            .Type<StringType>()
            .Description("Full name of the airport");
    }
}

public class AirportInfoType : ObjectType<AirportInfo>
{
    protected override void Configure(IObjectTypeDescriptor<AirportInfo> descriptor)
    {
        descriptor.Description("Grouped information about departure and arrival airports");

        descriptor.Field(a => a.Departure)
            .Type<ListType<AirportDetailType>>()
            .Description("List of departure airport details");

        descriptor.Field(a => a.Arrival)
            .Type<ListType<AirportDetailType>>()
            .Description("List of arrival airport details");
    }
}

public class AirportType : ObjectType<Airport>
{
    protected override void Configure(IObjectTypeDescriptor<Airport> descriptor)
    {
        descriptor.Field(a => a.Name);
        descriptor.Field(a => a.Code).Type<StringType>();
        descriptor.Field(a => a.Time);
    }
}

public class FlightInputType : InputObjectType<GetFlightRequest>
{
    protected override void Configure(IInputObjectTypeDescriptor<GetFlightRequest> descriptor)
    {
        descriptor.Name("GetFlightsRequest");
        descriptor.Description("Input parameters for querying flight information");

        descriptor.Field(f => f.FlightSearch)
            .Type<InputObjectType<FlightSearchQuery>>()
            .Description("The main search query containing departure and arrival airport codes. Optional for MultiCity flights.");

        descriptor.Field(f => f.Localization)
            .Type<InputObjectType<Localization>>()
            .Description("Localization options such as language, country, and currency.");

        descriptor.Field(f => f.AdvancedOptions)
            .Type<NonNullType<AdvancedSearchOptionsInputType>>()
            .Description("Advanced options including flight type, travel class, dates, and multi-city legs.");

        descriptor.Field(f => f.Passengers)
            .Type<InputObjectType<PassengerInfo>>()
            .Description("Passenger details including adults, children, and infants.");

        descriptor.Field(f => f.Sorting)
            .Type<SortingOptionsInputType>()
            .Description("Optional sorting preferences for the flight results.");

        descriptor.Field(f => f.Filters)
            .Type<AdvancedFiltersInputType>()
            .Description("Optional advanced filters such as airline inclusion/exclusion, bags, emissions, etc.");

        descriptor.Field(f => f.NextFlights)
            .Type<InputObjectType<NextFlights>>()
            .Description("Optional token to fetch next page of flights.");

        descriptor.Field(f => f.Booking)
            .Type<InputObjectType<BookingFlights>>()
            .Description("Optional booking token to initiate booking flow.");
    }
}

public class FlightOptionType : ObjectType<FlightOption>
{
    protected override void Configure(IObjectTypeDescriptor<FlightOption> descriptor)
    {
        descriptor.Description("A flight option including segments, price, and booking information");

        descriptor.Field(f => f.Flights)
            .Type<ListType<FlightSegmentType>>()
            .Description("List of flight segments that make up this option");

        descriptor.Field(f => f.Layovers)
            .Type<ListType<LayoverType>>()
            .Description("List of layovers between flight segments");

        descriptor.Field(f => f.TotalDuration)
            .Type<IntType>()
            .Description("Total flight duration in minutes");

        descriptor.Field(f => f.CarbonEmissions)
            .Type<ObjectType<CarbonEmissions>>()
            .Description("Carbon emissions information for the flight");

        descriptor.Field(f => f.Price)
            .Type<DecimalType>()
            .Description("Total price for the flight option");

        descriptor.Field(f => f.Type)
            .Type<StringType>()
            .Description("Type of flight (e.g., direct, connecting)");

        descriptor.Field(f => f.AirlineLogo)
            .Type<StringType>()
            .Description("URL to the airline logo");

        descriptor.Field(f => f.BookingToken)
            .Type<StringType>()
            .Description("Token used for booking this flight option");

        descriptor.Field(f => f.DepartureToken)
            .Type<StringType>()
            .Description("Token for fetching return flights paired with this outbound selection");

        descriptor.Field(f => f.BuyUrl)
            .Type<StringType>()
            .Description("Direct booking URL when available");

    }
}

public class FlightBookingOptionType : ObjectType<FlightBookingOption>
{
    protected override void Configure(IObjectTypeDescriptor<FlightBookingOption> descriptor)
    {
        descriptor.Description("Booking option grouping for together or split itineraries");

        descriptor.Field(f => f.Together)
            .Type<FlightBookingOptionDetailType>()
            .Description("Booking option for the full itinerary together");

        descriptor.Field(f => f.Departing)
            .Type<FlightBookingOptionDetailType>()
            .Description("Booking option for the departing leg only");

        descriptor.Field(f => f.Returning)
            .Type<FlightBookingOptionDetailType>()
            .Description("Booking option for the return leg only");

        descriptor.Field(f => f.SeparateTickets)
            .Type<BooleanType>()
            .Description("Whether legs are booked as separate tickets");
    }
}

public class FlightBookingOptionDetailType : ObjectType<FlightBookingOptionDetail>
{
    protected override void Configure(IObjectTypeDescriptor<FlightBookingOptionDetail> descriptor)
    {
        descriptor.Description("Booking option provider details");

        descriptor.Field(f => f.BookWith)
            .Type<StringType>()
            .Description("Provider name to book with");

        descriptor.Field(f => f.Price)
            .Type<DecimalType>()
            .Description("Price offered by this provider");

        descriptor.Field(f => f.AirlineLogo)
            .Type<StringType>()
            .Description("Logo URL for the provider or airline");

        descriptor.Field(f => f.BookingRequest)
            .Type<FlightBookingRequestType>()
            .Description("Booking request payload");

        descriptor.Field(f => f.Airline)
            .Type<BooleanType>()
            .Description("Whether this is a direct airline booking");

        descriptor.Field(f => f.MarketedAs)
            .Type<ListType<StringType>>()
            .Description("Flight numbers marketed for this booking");

        descriptor.Field(f => f.BaggagePrices)
            .Type<ListType<StringType>>()
            .Description("Baggage pricing info per provider");
    }
}

public class FlightBookingRequestType : ObjectType<FlightBookingRequest>
{
    protected override void Configure(IObjectTypeDescriptor<FlightBookingRequest> descriptor)
    {
        descriptor.Description("Booking request link and post data");

        descriptor.Field(f => f.Url)
            .Type<StringType>()
            .Description("Booking URL");

        descriptor.Field(f => f.PostData)
            .Type<StringType>()
            .Description("POST data payload for booking");
    }
}

public class FlightPriceHistoryPointType : ObjectType<FlightPriceHistoryPoint>
{
    protected override void Configure(IObjectTypeDescriptor<FlightPriceHistoryPoint> descriptor)
    {
        descriptor.Description("A single point in the flight price history");

        descriptor.Field(p => p.Date)
            .Type<StringType>()
            .Description("Date for this price point");

        descriptor.Field(p => p.Price)
            .Type<DecimalType>()
            .Description("Price recorded on this date");
    }
}

public class FlightPriceInsightsType : ObjectType<FlightPriceInsights>
{
    protected override void Configure(IObjectTypeDescriptor<FlightPriceInsights> descriptor)
    {
        descriptor.Description("Price analytics and historical data for the requested route");

        descriptor.Field(p => p.LowestPrice)
            .Type<DecimalType>()
            .Description("Lowest recorded price for this route");

        descriptor.Field(p => p.PriceLevel)
            .Type<StringType>()
            .Description("Assessment of current price (e.g., 'low', 'average', 'high')");

        descriptor.Field(p => p.TypicalPriceRange)
            .Type<ListType<DecimalType>>()
            .Description("Typical price range for this route");

        descriptor
            .Field("priceHistory")
            .ResolveWith<FlightPriceInsightsResolvers>(r => r.GetPriceHistory(default!))
            .Type<ListType<FlightPriceHistoryPointType>>()
            .Description("Historical price points for this route");
    }
}

public class FlightPriceInsightsResolvers
{
    public IEnumerable<FlightPriceHistoryPoint> GetPriceHistory([Parent] FlightPriceInsights priceInsights)
    {
        if (priceInsights.PriceHistory == null)
        {
            return new List<FlightPriceHistoryPoint>();
        }

        return priceInsights.PriceHistory.Select(point => new FlightPriceHistoryPoint
        {
            Date = ConvertTimestampToDate(point[0]),
            Price = point[1] switch
            {
                JsonElement jsonElement => jsonElement.ValueKind == JsonValueKind.Number ? jsonElement.GetDecimal() : 0,
                null => 0,
                _ => Convert.ToDecimal(point[1], CultureInfo.InvariantCulture)
            }
        });
    }

    private static string ConvertTimestampToDate(object? timestamp)
    {
        if (string.IsNullOrEmpty(timestamp?.ToString()))
        {
            return string.Empty;
        }

        if (timestamp is not JsonElement { ValueKind: JsonValueKind.Number } jsonElement ||
            !jsonElement.TryGetInt64(out var unixTimestamp))
        {
            return timestamp.ToString() ?? string.Empty;
        }

        try
        {
            var dateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
            return dateTimeOffset.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        }
        catch
        {
            return jsonElement.ToString();
        }
    }
}

public class FlightSearchMetadataType : ObjectType<SearchMetadata>
{
    protected override void Configure(IObjectTypeDescriptor<SearchMetadata> descriptor)
    {
        descriptor.Description("Metadata about the flight search request and processing");

        descriptor.Field(s => s.Id)
            .Type<StringType>()
            .Description("Unique identifier for this search");

        descriptor.Field(s => s.Status)
            .Type<StringType>()
            .Description("Status of the search request");

        descriptor.Field(s => s.JsonEndpoint)
            .Type<StringType>()
            .Description("JSON endpoint URL for the search results");

        descriptor.Field(s => s.CreatedAt)
            .Type<StringType>()
            .Description("Timestamp when the search was created");

        descriptor.Field(s => s.ProcessedAt)
            .Type<StringType>()
            .Description("Timestamp when the search was processed");

        descriptor.Field(s => s.RawHtmlFile)
            .Type<StringType>()
            .Description("URL to the raw HTML file of the search results");

        descriptor.Field(s => s.PrettifyHtmlFile)
            .Type<StringType>()
            .Description("URL to the prettified HTML file of the search results");

        descriptor.Field(s => s.TotalTimeTaken)
            .Type<DecimalType>()
            .Description("Total time taken to process the search in seconds");
    }
}

public class FlightSearchParametersType : ObjectType<FlightSearchParameters>
{
    protected override void Configure(IObjectTypeDescriptor<FlightSearchParameters> descriptor)
    {
        descriptor.Description("Parameters used for the flight search");

        descriptor.Field(s => s.Engine)
            .Type<StringType>()
            .Description("Search engine used for the flight search");

        descriptor.Field(s => s.LanguageCode)
            .Type<StringType>()
            .Description("Language code (hl parameter) for localization");

        descriptor.Field(s => s.CountryCode)
            .Type<StringType>()
            .Description("Country code (gl parameter) for localization");

        descriptor.Field(s => s.Type)
            .Type<StringType>()
            .Description("Type of flight search (e.g., 'one-way', 'round-trip')");

        descriptor.Field(s => s.DepartureId)
            .Type<StringType>()
            .Description("Identifier for the departure airport");

        descriptor.Field(s => s.ArrivalId)
            .Type<StringType>()
            .Description("Identifier for the arrival airport");

        descriptor.Field(s => s.OutboundDate)
            .Type<StringType>()
            .Description("Date for the outbound flight (YYYY-MM-DD format)");

        descriptor.Field(s => s.TravelClass)
            .Type<TravelClassEnum>()
            .Description("Travel class for the flight search");

        descriptor.Field(s => s.Adults)
            .Type<IntType>()
            .Description("Count of adults");

        descriptor.Field(s => s.Currency)
            .Type<StringType>()
            .Description("Country currency for localization");
    }
}

public class FlightSegmentType : ObjectType<FlightSegment>
{
    protected override void Configure(IObjectTypeDescriptor<FlightSegment> descriptor)
    {
        descriptor.Description("A segment of a flight itinerary representing a single flight");

        descriptor.Field(f => f.DepartureAirport)
            .Type<AirportType>()
            .Description("Departure airport information");

        descriptor.Field(f => f.ArrivalAirport)
            .Type<AirportType>()
            .Description("Arrival airport information");

        descriptor.Field(f => f.Duration)
            .Type<IntType>()
            .Description("Duration of the flight segment in minutes");

        descriptor.Field(f => f.Airplane)
            .Type<StringType>()
            .Description("Aircraft model used for this flight segment");

        descriptor.Field(f => f.Airline)
            .Type<StringType>()
            .Description("Airline operating this flight segment");

        descriptor.Field(f => f.AirlineLogo)
            .Type<StringType>()
            .Description("URL to the airline logo");

        descriptor.Field(f => f.TravelClass)
            .Type<StringType>()
            .Description("Travel class for this flight segment");

        descriptor.Field(f => f.FlightNumber)
            .Type<StringType>()
            .Description("Flight number for this segment");

        descriptor.Field(f => f.TicketAlsoSoldBy)
            .Type<ListType<StringType>>()
            .Description("List of other airlines selling tickets for this flight");

        descriptor.Field(f => f.Legroom)
            .Type<StringType>()
            .Description("Legroom information for this flight segment");

        descriptor.Field(f => f.Extensions)
            .Type<ListType<StringType>>()
            .Description("Additional flight segment information");
    }
}

public class LayoverType : ObjectType<Layover>
{
    protected override void Configure(IObjectTypeDescriptor<Layover> descriptor)
    {
        descriptor.Description("A layover between flight segments");

        descriptor.Field(l => l.Duration)
            .Type<IntType>()
            .Description("Duration of the layover in minutes");

        descriptor.Field(l => l.Name)
            .Type<StringType>()
            .Description("Name of the layover airport");

        descriptor.Field(l => l.Id)
            .Type<StringType>()
            .Description("Identifier for the layover airport");
    }
}

public class FlightPriceHistoryPoint
{
    public string? Date { get; set; }
    public decimal Price { get; set; }
}

public class AdvancedSearchOptionsInputType : InputObjectType<AdvancedSearchOptions>
{
    protected override void Configure(IInputObjectTypeDescriptor<AdvancedSearchOptions> descriptor)
    {
        descriptor.Name("AdvancedSearchOptions");
        descriptor.Description("Advanced options including flight type, travel class, dates, and multi-city legs");

        descriptor.Field(f => f.Type)
            .Type<FlightTypeEnum>()
            .Description("Type of flight search (round trip, one way, or multi-city)");

        descriptor.Field(f => f.OutboundDate)
            .Type<StringType>()
            .Description("Date for the outbound flight (YYYY-MM-DD format). Required for OneWay and RoundTrip, not used for MultiCity.");

        descriptor.Field(f => f.ReturnDate)
            .Type<StringType>()
            .Description("Date for the return flight (YYYY-MM-DD format)");

        descriptor.Field(f => f.TravelClass)
            .Type<TravelClassEnum>()
            .Description("Travel class for the flight");

        descriptor.Field(f => f.MultiCityJson)
            .Type<ListType<InputObjectType<MultiCityLeg>>>()
            .Description("Multi-city flight legs (for multi-city searches)");

        descriptor.Field(f => f.ShowHidden)
            .Type<BooleanType>()
            .Description("Whether to show hidden flight options");

        descriptor.Field(f => f.DeepSearch)
            .Type<BooleanType>()
            .Description("Whether to perform deep search for more options");
    }
}

public class SortingOptionsInputType : InputObjectType<SortingOptions>
{
    protected override void Configure(IInputObjectTypeDescriptor<SortingOptions> descriptor)
    {
        descriptor.Name("SortingOptions");
        descriptor.Description("Sorting preferences for flight results");

        descriptor.Field(f => f.SortBy)
            .Type<SortByEnum>()
            .Description("Sort criteria for flight results");
    }
}

public class AdvancedFiltersInputType : InputObjectType<AdvancedFilters>
{
    protected override void Configure(IInputObjectTypeDescriptor<AdvancedFilters> descriptor)
    {
        descriptor.Name("AdvancedFilters");
        descriptor.Description("Advanced filters such as airline inclusion/exclusion, bags, emissions, etc.");

        descriptor.Field(f => f.Stops)
            .Type<StopsEnum>()
            .Description("Number of stops filter");

        descriptor.Field(f => f.ExcludeAirlines)
            .Type<StringType>()
            .Description("Comma-separated list of airline codes to exclude");

        descriptor.Field(f => f.IncludeAirlines)
            .Type<StringType>()
            .Description("Comma-separated list of airline codes to include");

        descriptor.Field(f => f.Bags)
            .Type<IntType>()
            .Description("Number of bags filter");

        descriptor.Field(f => f.MaxPrice)
            .Type<IntType>()
            .Description("Maximum price filter");

        descriptor.Field(f => f.OutboundTimes)
            .Type<StringType>()
            .Description("Outbound departure time filters");

        descriptor.Field(f => f.ReturnTimes)
            .Type<StringType>()
            .Description("Return departure time filters");

        descriptor.Field(f => f.Emissions)
            .Type<IntType>()
            .Description("Emissions filter (1 for less emissions only)");

        descriptor.Field(f => f.LayoverDuration)
            .Type<StringType>()
            .Description("Layover duration filter in 'min,max' format");

        descriptor.Field(f => f.ExcludeConns)
            .Type<StringType>()
            .Description("Comma-separated list of airport codes to exclude for connections");

        descriptor.Field(f => f.MaxDuration)
            .Type<IntType>()
            .Description("Maximum flight duration filter in minutes");
    }
}

// GraphQL Enum Types
public class FlightTypeEnum : EnumType<Contracts.Enums.FlightType>
{
    protected override void Configure(IEnumTypeDescriptor<Contracts.Enums.FlightType> descriptor)
    {
        descriptor.Name("FlightType");
        descriptor.Description("Types of flight itineraries");
        descriptor.Value(Contracts.Enums.FlightType.RoundTrip).Name("RoundTrip");
        descriptor.Value(Contracts.Enums.FlightType.OneWay).Name("OneWay");
        descriptor.Value(Contracts.Enums.FlightType.MultiCity).Name("MultiCity");
    }
}

public class TravelClassEnum : EnumType<TravelClassType>
{
    protected override void Configure(IEnumTypeDescriptor<TravelClassType> descriptor)
    {
        descriptor.Name("TravelClass");
        descriptor.Description("Travel class options for flights");
        descriptor.Value(TravelClassType.Economy).Name("Economy");
        descriptor.Value(TravelClassType.PremiumEconomy).Name("PremiumEconomy");
        descriptor.Value(TravelClassType.Business).Name("Business");
        descriptor.Value(TravelClassType.First).Name("First");
    }
}

public class SortByEnum : EnumType<FlightSortByType>
{
    protected override void Configure(IEnumTypeDescriptor<FlightSortByType> descriptor)
    {
        descriptor.Name("SortBy");
        descriptor.Description("Sort options for flight search results");
        descriptor.Value(FlightSortByType.TopFlights).Name("TopFlights");
        descriptor.Value(FlightSortByType.Price).Name("Price");
        descriptor.Value(FlightSortByType.DepartureTime).Name("DepartureTime");
        descriptor.Value(FlightSortByType.ArrivalTime).Name("ArrivalTime");
        descriptor.Value(FlightSortByType.Duration).Name("Duration");
        descriptor.Value(FlightSortByType.Emissions).Name("Emissions");
    }
}

public class StopsEnum : EnumType<StopsType>
{
    protected override void Configure(IEnumTypeDescriptor<StopsType> descriptor)
    {
        descriptor.Name("Stops");
        descriptor.Description("Number of stops during the flight");
        descriptor.Value(StopsType.Any).Name("Any");
        descriptor.Value(StopsType.Nonstop).Name("Nonstop");
        descriptor.Value(StopsType.OneStopOrFewer).Name("OneStopOrFewer");
        descriptor.Value(StopsType.TwoStopsOrFewer).Name("TwoStopsOrFewer");
    }
}

