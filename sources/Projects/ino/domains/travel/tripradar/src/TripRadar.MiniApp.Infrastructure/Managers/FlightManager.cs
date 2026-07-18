using System.Globalization;
using TripRadar.MiniApp.Client.Infrastructure.Contracts;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Client.Infrastructure.Managers;

public sealed class FlightManager(TripRadarApiClient client) : IFlightManager
{
    public async Task<FlightSearchResult?> SearchAsync(
        FlightSearchParams p,
        string? departureToken = null,
        CancellationToken ct = default)
    {
        var request = new
        {
            flightSearch = new { departureId = p.DepartureId, arrivalId = p.ArrivalId },
            advancedOptions = new
            {
                type = p.Type switch
                {
                    FlightType.RoundTrip => "RoundTrip",
                    FlightType.OneWay => "OneWay",
                    FlightType.MultiCity => "MultiCity",
                    _ => "RoundTrip"
                },
                outboundDate = p.OutboundDate,
                returnDate = p.Type == FlightType.RoundTrip ? p.ReturnDate : null,
                travelClass = MapTravelClass(p.TravelClass)
            },
            passengers = new
            {
                adults = p.Adults,
                children = p.Children,
                infantsInSeat = p.Infants
            },
            localization = GetLocalization(),
            nextFlights = departureToken is not null ? new { departureToken } : null
        };

        var wrapper = await client.GraphQlAsync<FlightsWrapper>(
            GraphQlQueries.SearchFlights,
            new { request },
            ct);

        return wrapper?.Flights;
    }

    public async Task<FlightBookingResponse?> GetBookingAsync(string bookingToken, FlightSearchParams searchParams)
    {
        var request = new
        {
            flightSearch = new
            {
                departureId = searchParams.DepartureId.Split(',')[0],
                arrivalId = searchParams.ArrivalId.Split(',')[0]
            },
            booking = new { bookingToken },
            advancedOptions = new
            {
                type = searchParams.Type switch
                {
                    FlightType.RoundTrip => "RoundTrip",
                    FlightType.OneWay => "OneWay",
                    FlightType.MultiCity => "MultiCity",
                    _ => "RoundTrip"
                },
                outboundDate = searchParams.OutboundDate,
                returnDate = searchParams.Type == FlightType.RoundTrip ? searchParams.ReturnDate : null,
                travelClass = MapTravelClass(searchParams.TravelClass)
            },
            localization = GetLocalization()
        };

        var wrapper = await client.GraphQlAsync<FlightsBookingRawWrapper>(
            GraphQlQueries.GetFlightBooking,
            new { request });

        if (wrapper?.Flights is not { } raw)
            return null;

        var options = raw.BookingOptions?
            .SelectMany(g => new[] { (g.Together, g.SeparateTickets), (g.Departing, g.SeparateTickets), (g.Returning, g.SeparateTickets) })
            .Where(pair => pair.Item1 is not null)
            .Select(pair => new FlightBookingOption(
                pair.Item1!.BookWith,
                pair.Item1.Price,
                pair.Item1.AirlineLogo,
                pair.Item1.BookingRequest?.Url,
                pair.Item1.BookingRequest?.PostData,
                pair.Item1.Airline ?? false,
                pair.Item1.MarketedAs,
                pair.Item1.BaggagePrices,
                pair.SeparateTickets ?? false))
            .ToList();

        return new FlightBookingResponse(raw.BestFlights, options);
    }

    private static string MapTravelClass(TravelClass travelClass) => travelClass switch
    {
        TravelClass.Economy => "Economy",
        TravelClass.PremiumEconomy => "PremiumEconomy",
        TravelClass.Business => "Business",
        TravelClass.First => "First",
        _ => "Economy"
    };

    private static object GetLocalization()
    {
        var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return new
        {
            gl = lang == "ru" ? "ru" : "us",
            hl = lang,
            currency = lang == "ru" ? "RUB" : "USD"
        };
    }

    private sealed record FlightsWrapper(FlightSearchResult Flights);
    private sealed record FlightsBookingRawWrapper(GraphQlBookingResponse Flights);
}