namespace TripRadar.Server.Application.Constants.Flights;

public static class SavedFlightQueryColumnNameConstants
{
    // Root level properties
    public const string SearchMetadata = "search_metadata";
    public const string SearchParameters = "search_parameters";
    public const string SearchInformation = "search_information";
    public const string BestFlights = "best_flights";
    public const string OtherFlights = "other_flights";
    public const string SerpapiPagination = "serpapi_pagination";
    public const string Airport = "airport";
    public const string CarbonEmissions = "carbon_emissions";
    public const string Layovers = "layovers";
    public const string PriceInsights = "price_insights";
    public const string Airports = "airports";

    // Search metadata properties
    public const string CreatedAt = "created_at";
    public const string Status = "status";
    public const string ProcessedAt = "processed_at";
    public const string TotalTimeTaken = "total_time_taken";

    // Search parameters properties
    public const string Engine = "engine";
    public const string LanguageCode = "hl";
    public const string CountryCode = "gl";
    public const string Type = "type";
    public const string DepartureId = "departure_id";
    public const string ArrivalId = "arrival_id";
    public const string OutboundDate = "outbound_date";
    public const string Adults = "adults";
    public const string Currency = "currency";

    // Best flights properties
    public const string Flights = "flights";
    public const string BestFlightsLayovers = "layovers";
    public const string TotalDuration = "total_duration";
    public const string BestFlightsCarbonEmissions = "carbon_emissions";
    public const string Price = "price";
    public const string FlightType = "type";
    public const string AirlineLogo = "airline_logo";
    public const string BookingToken = "booking_token";

    // Flight properties
    public const string DepartureAirport = "departure_airport";
    public const string ArrivalAirport = "arrival_airport";
    public const string Duration = "duration";
    public const string Airplane = "airplane";
    public const string Airline = "airline";
    public const string FlightAirlineLogo = "airline_logo";
    public const string TravelClass = "travel_class";
    public const string FlightNumber = "flight_number";
    public const string TicketAlsoSoldBy = "ticket_also_sold_by";
    public const string Legroom = "legroom";
    public const string Extensions = "extensions";
    public const string PlaneAndCrewBy = "plane_and_crew_by";

    // Airport properties
    public const string AirportName = "name";
    public const string AirportId = "id";
    public const string AirportTime = "time";
    public const string City = "city";
    public const string Country = "country";
    public const string Image = "image";
    public const string Thumbnail = "thumbnail";
    public const string Departure = "departure";
    public const string Arrival = "arrival";

    // Carbon emissions properties
    public const string ThisFlight = "this_flight";
    public const string TypicalForThisRoute = "typical_for_this_route";
    public const string DifferencePercent = "difference_percent";

    // Layover properties
    public const string LayoverDuration = "duration";
    public const string LayoverName = "name";
    public const string LayoverId = "id";

    // Price insights properties
    public const string LowestPrice = "lowest_price";
    public const string PriceLevel = "price_level";
    public const string TypicalPriceRange = "typical_price_range";
    public const string PriceHistory = "price_history";
}
