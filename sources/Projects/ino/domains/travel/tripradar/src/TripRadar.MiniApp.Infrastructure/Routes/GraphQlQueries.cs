namespace TripRadar.MiniApp.Client.Infrastructure.Routes;

public static class GraphQlQueries
{
    public const string SearchFlights = """
        query SearchFlights($request: GetFlightsRequest!) {
            flights(request: $request) {
                bestFlights {
                    flights { departureAirport { name code time } arrivalAirport { name code time } duration airplane airline airlineLogo travelClass flightNumber legroom extensions }
                    layovers { duration name id }
                    totalDuration price type airlineLogo bookingToken departureToken
                    carbonEmissions { thisFlight typicalForThisRoute differencePercent }
                }
                otherFlights {
                    flights { departureAirport { name code time } arrivalAirport { name code time } duration airplane airline airlineLogo travelClass flightNumber legroom extensions }
                    layovers { duration name id }
                    totalDuration price type airlineLogo bookingToken departureToken
                    carbonEmissions { thisFlight typicalForThisRoute differencePercent }
                }
                priceInsights { lowestPrice priceLevel typicalPriceRange priceHistory { date price } }
                airports { departure { airport { id name } city country countryCode image thumbnail } arrival { airport { id name } city country countryCode image thumbnail } }
            }
        }
        """;

    public const string SearchHotels = """
        query SearchHotels($request: GetHotelRequest!) {
            hotels(request: $request) {
                searchInformation { totalResults }
                properties {
                    type name description link propertyToken
                    gpsCoordinates { latitude longitude }
                    checkInTime checkOutTime
                    ratePerNight { lowest extractedLowest beforeTaxesFees extractedBeforeTaxesFees }
                    totalRate { lowest extractedLowest beforeTaxesFees extractedBeforeTaxesFees }
                    deal dealDescription hotelClass extractedHotelClass
                    images { thumbnail originalImage }
                    overallRating reviews locationRating
                    amenities nearbyPlaces { name transportations { type duration } }
                    prices { source logo numGuests ratePerNight { lowest extractedLowest } freeCancellation }
                    ecoCertified
                }
                brands { id name children { id name } }
                serpapiPagination { currentFrom currentTo nextPageToken }
            }
        }
        """;

    public const string FlightExplore = """
        query FlightExplore($request: GetFlightExploreRequestInput!) {
            flightExplore(request: $request) {
                destinations {
                    name country thumbnail flightPrice hotelPrice
                    flightDuration numberOfStops airline
                    startDate endDate
                    destinationAirport { id name }
                }
            }
        }
        """;

    public const string FlightPriceCalendar = """
        query FlightPriceCalendar($request: GetFlightPriceCalendarRequestInput!) {
            flightPriceCalendar(request: $request) {
                days { date lowestPrice currency }
                cheapestDate cheapestPrice
            }
        }
        """;


    public const string GetFlightBooking = """
        query GetFlightBooking($request: GetFlightsRequest!) {
            flights(request: $request) {
                bestFlights {
                    flights { departureAirport { name code time } arrivalAirport { name code time } duration airline airlineLogo flightNumber airplane travelClass legroom extensions }
                    totalDuration price airlineLogo bookingToken
                    carbonEmissions { thisFlight typicalForThisRoute differencePercent }
                }
                bookingOptions {
                    separateTickets
                    together { bookWith price airlineLogo airline marketedAs baggagePrices bookingRequest { url postData } }
                    departing { bookWith price airlineLogo airline marketedAs baggagePrices bookingRequest { url postData } }
                    returning { bookWith price airlineLogo airline marketedAs baggagePrices bookingRequest { url postData } }
                }
            }
        }
        """;
}