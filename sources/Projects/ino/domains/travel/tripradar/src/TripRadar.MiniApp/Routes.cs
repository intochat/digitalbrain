namespace TripRadar.MiniApp;

public static class AppRoutes
{
    public const string Home = "/";
    public const string Auth = "/auth";
    public const string Account = "/account";
    public const string Alerts = "/alerts";
    public const string Companion = "/companion";
    public const string NotFound = "/not-found";
    public const string PriceGraph = "/price-graph";

    public const string Flights = "/flights";
    public const string FlightResults = "/flights/results";
    public const string FlightCitySearch = "/flights/city-search/{Field}";
    public const string FlightAirportDetail = "/flights/airport-detail/{Field}";
    public const string FlightBooking = "/flights/booking/{Token}";
    public const string FlightTrip = "/flights/trip/{Token}";

    public const string Hotels = "/hotels";
    public const string HotelResults = "/hotels/results";
    public const string HotelDetails = "/hotels/{Token}";

    public static string FlightCitySearchFor(string field) => $"/flights/city-search/{Uri.EscapeDataString(field)}";
    public static string FlightAirportDetailFor(string field) => $"/flights/airport-detail/{Uri.EscapeDataString(field)}";
    public static string FlightBookingFor(string token) => $"/flights/booking/{Uri.EscapeDataString(token)}";
    public static string FlightTripFor(string token) => $"/flights/trip/{Uri.EscapeDataString(token)}";
    public static string HotelDetailsFor(string token) => $"/hotels/{Uri.EscapeDataString(token)}";
}
