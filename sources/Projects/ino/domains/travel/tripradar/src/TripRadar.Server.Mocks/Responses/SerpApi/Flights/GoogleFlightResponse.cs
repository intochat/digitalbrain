using System.Text.Json.Serialization;

namespace TripRadar.Server.Mocks.Responses.SerpApi.Flights;

public class GoogleFlightResponse
{
    public SearchMetadata? SearchMetadata { get; set; }
    public SearchParameters? SearchParameters { get; set; }
    public List<FlightOption>? BestFlights { get; set; }
    public List<FlightOption>? OtherFlights { get; set; }
    public PriceInsights? PriceInsights { get; set; }
    public List<AirportInfo>? Airports { get; set; }
}

public class AirportDetail
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Timezone { get; set; }
}

public class AirportInfo
{
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public AirportDetail? Detail { get; set; }
}

public class FlightOption
{
    public List<FlightSegment>? Flights { get; set; }
    public List<Layover>? Layovers { get; set; }
    public int TotalDuration { get; set; }
    public decimal Price { get; set; }
    public string? Type { get; set; }
    public string? AirlineLogo { get; set; }
    [JsonPropertyName("booking_token")]
    public string? BookingToken { get; set; }
}

public class FlightSegment
{
    public string? DepartureAirport { get; set; }
    public string? ArrivalAirport { get; set; }
    public string? DepartureTime { get; set; }
    public string? ArrivalTime { get; set; }
    public string? Airline { get; set; }
    public string? FlightNumber { get; set; }
    public string? Duration { get; set; }
}

public class Layover
{
    public string? Airport { get; set; }
    public int Duration { get; set; }
}

public class PriceHistoryPoint
{
    public string? Date { get; set; }
    public decimal Price { get; set; }
}

public class PriceInsights
{
    public List<PriceHistoryPoint> PriceHistory { get; set; } = new();
}

public class SearchMetadata
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public string? CreatedAt { get; set; }
}

public class SearchParameters
{
    public string Engine { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string DepartureId { get; set; } = string.Empty;
    public string ArrivalId { get; set; } = string.Empty;
    public string OutboundDate { get; set; } = string.Empty;
    public int TravelClass { get; set; }
}
