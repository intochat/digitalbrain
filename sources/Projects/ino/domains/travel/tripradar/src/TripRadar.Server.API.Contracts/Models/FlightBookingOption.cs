using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class FlightBookingOption
{
    [JsonPropertyName("together")]
    public FlightBookingOptionDetail? Together { get; set; }

    [JsonPropertyName("departing")]
    public FlightBookingOptionDetail? Departing { get; set; }

    [JsonPropertyName("returning")]
    public FlightBookingOptionDetail? Returning { get; set; }

    [JsonPropertyName("separate_tickets")]
    public bool? SeparateTickets { get; set; }
}

public class FlightBookingOptionDetail
{
    [JsonPropertyName("book_with")]
    public string? BookWith { get; set; }

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("airline_logo")]
    public string? AirlineLogo { get; set; }

    [JsonPropertyName("booking_request")]
    public FlightBookingRequest? BookingRequest { get; set; }

    [JsonPropertyName("airline")]
    public bool? Airline { get; set; }

    [JsonPropertyName("marketed_as")]
    public List<string>? MarketedAs { get; set; }

    [JsonPropertyName("baggage_prices")]
    public List<string>? BaggagePrices { get; set; }
}

public class FlightBookingRequest
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("post_data")]
    public string? PostData { get; set; }
}
