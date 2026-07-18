using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Responses;

/// <summary>
/// Application DTO for Google Travel Explore API response.
/// </summary>
public class GetFlightExploreResponseDTO
{
    [JsonPropertyName("search_metadata")]
    public FlightExploreSearchMetadataDTO? SearchMetadata { get; set; }

    [JsonPropertyName("search_parameters")]
    public FlightExploreSearchParametersDTO? SearchParameters { get; set; }

    [JsonPropertyName("destinations")]
    public List<FlightExploreDestinationDTO>? Destinations { get; set; }

    [JsonPropertyName("start_date")]
    public string? StartDate { get; set; }

    [JsonPropertyName("end_date")]
    public string? EndDate { get; set; }

    [JsonPropertyName("flights")]
    public List<FlightExploreResultDTO>? Flights { get; set; }
}

public class FlightExploreSearchMetadataDTO
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("json_endpoint")]
    public string? JsonEndpoint { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("processed_at")]
    public string? ProcessedAt { get; set; }

    [JsonPropertyName("google_url")]
    public string? GoogleUrl { get; set; }

    [JsonPropertyName("raw_html_file")]
    public string? RawHtmlFile { get; set; }

    [JsonPropertyName("prettify_html_file")]
    public string? PrettifyHtmlFile { get; set; }

    [JsonPropertyName("total_time_taken")]
    public double? TotalTimeTaken { get; set; }
}

public class FlightExploreSearchParametersDTO
{
    [JsonPropertyName("engine")]
    public string? Engine { get; set; }

    [JsonPropertyName("departure_id")]
    public string? DepartureId { get; set; }

    [JsonPropertyName("arrival_id")]
    public string? ArrivalId { get; set; }

    [JsonPropertyName("arrival_area_id")]
    public string? ArrivalAreaId { get; set; }

    [JsonPropertyName("gl")]
    public string? Gl { get; set; }

    [JsonPropertyName("hl")]
    public string? Hl { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("type")]
    public int? Type { get; set; }

    [JsonPropertyName("outbound_date")]
    public string? OutboundDate { get; set; }

    [JsonPropertyName("return_date")]
    public string? ReturnDate { get; set; }

    [JsonPropertyName("month")]
    public int? Month { get; set; }

    [JsonPropertyName("travel_duration")]
    public int? TravelDuration { get; set; }

    [JsonPropertyName("travel_class")]
    public int? TravelClass { get; set; }

    [JsonPropertyName("adults")]
    public int? Adults { get; set; }

    [JsonPropertyName("children")]
    public int? Children { get; set; }

    [JsonPropertyName("infants_in_seat")]
    public int? InfantsInSeat { get; set; }

    [JsonPropertyName("infants_on_lap")]
    public int? InfantsOnLap { get; set; }

    [JsonPropertyName("stops")]
    public int? Stops { get; set; }

    [JsonPropertyName("travel_mode")]
    public int? TravelMode { get; set; }

    [JsonPropertyName("interest")]
    public string? Interest { get; set; }

    [JsonPropertyName("include_airlines")]
    public string? IncludeAirlines { get; set; }

    [JsonPropertyName("bags")]
    public int? Bags { get; set; }

    [JsonPropertyName("max_price")]
    public int? MaxPrice { get; set; }

    [JsonPropertyName("max_duration")]
    public int? MaxDuration { get; set; }
}

public class FlightExploreDestinationDTO
{
    [JsonPropertyName("destination_id")]
    public string? DestinationId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("gps_coordinates")]
    public FlightExploreGpsCoordinatesDTO? GpsCoordinates { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }

    [JsonPropertyName("destination_airport")]
    public FlightExploreAirportDTO? DestinationAirport { get; set; }

    [JsonPropertyName("start_date")]
    public string? StartDate { get; set; }

    [JsonPropertyName("end_date")]
    public string? EndDate { get; set; }

    [JsonPropertyName("flight_price")]
    public decimal? FlightPrice { get; set; }

    [JsonPropertyName("hotel_price")]
    public decimal? HotelPrice { get; set; }

    [JsonPropertyName("flight_duration")]
    public int? FlightDuration { get; set; }

    [JsonPropertyName("number_of_stops")]
    public int? NumberOfStops { get; set; }

    [JsonPropertyName("airline")]
    public string? Airline { get; set; }

    [JsonPropertyName("airline_code")]
    public string? AirlineCode { get; set; }

    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("serpapi_link")]
    public string? SerpApiLink { get; set; }
}

public class FlightExploreGpsCoordinatesDTO
{
    [JsonPropertyName("latitude")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; set; }
}

public class FlightExploreAirportDTO
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }
}

public class FlightExploreResultDTO
{
    [JsonPropertyName("departure_airport")]
    public FlightExploreAirportDTO? DepartureAirport { get; set; }

    [JsonPropertyName("arrival_airport")]
    public FlightExploreAirportDTO? ArrivalAirport { get; set; }

    [JsonPropertyName("duration")]
    public int? Duration { get; set; }

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("cheapest_flight")]
    public bool? CheapestFlight { get; set; }

    [JsonPropertyName("number_of_stops")]
    public int? NumberOfStops { get; set; }

    [JsonPropertyName("airline")]
    public string? Airline { get; set; }

    [JsonPropertyName("airline_code")]
    public string? AirlineCode { get; set; }
}
