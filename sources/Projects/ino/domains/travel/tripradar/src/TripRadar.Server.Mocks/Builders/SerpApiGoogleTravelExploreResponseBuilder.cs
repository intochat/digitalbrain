using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.Mocks.Builders;

public class SerpApiGoogleTravelExploreResponseBuilder
{
    private readonly ILogger<SerpApiGoogleTravelExploreResponseBuilder> _logger;
    private readonly Dictionary<string, GetFlightExploreResponseDTO> _mockTravelExploreData = new();

    public SerpApiGoogleTravelExploreResponseBuilder(ILogger<SerpApiGoogleTravelExploreResponseBuilder> logger)
    {
        _logger = logger;
        InitializeMockData();
    }

    public GetFlightExploreResponseDTO GetTravelExploreData(string departureId, string? arrivalId = null, string? arrivalAreaId = null)
    {
        var cacheKey = $"departure_id={departureId}&arrival_id={arrivalId ?? ""}&arrival_area_id={arrivalAreaId ?? ""}";

        if (_mockTravelExploreData.TryGetValue(cacheKey, out var mockData))
        {
            return mockData;
        }

        // Check for departure-based cache
        var departureKey = $"departure_id={departureId}";
        if (_mockTravelExploreData.TryGetValue(departureKey, out var departureMockData))
        {
            _logger.LogInformation("Returning mock travel explore data for departure {DepartureId}", departureId);
            return departureMockData;
        }

        _logger.LogInformation("Creating new mock travel explore data for departure {DepartureId}", departureId);
        var defaultData = CreateMockTravelExploreData(departureId);
        _mockTravelExploreData[departureKey] = defaultData;

        return defaultData;
    }

    private static GetFlightExploreResponseDTO CreateMockTravelExploreData(string departureId)
    {
        var searchMetadata = new FlightExploreSearchMetadataDTO
        {
            Id = Guid.NewGuid().ToString(),
            Status = "Success",
            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss UTC"),
            ProcessedAt = DateTime.UtcNow.AddSeconds(1).ToString("yyyy-MM-ddTHH:mm:ss UTC"),
            GoogleUrl = $"https://www.google.com/travel/explore?dep={departureId}",
            TotalTimeTaken = 1.5
        };

        var searchParameters = new FlightExploreSearchParametersDTO
        {
            Engine = "google_travel_explore",
            DepartureId = departureId,
            Gl = "us",
            Hl = "en",
            Currency = "USD"
        };

        var destinations = GenerateDestinations(departureId);

        return new GetFlightExploreResponseDTO
        {
            SearchMetadata = searchMetadata,
            SearchParameters = searchParameters,
            Destinations = destinations,
            StartDate = DateTime.Now.AddDays(14).ToString("yyyy-MM-dd"),
            EndDate = DateTime.Now.AddDays(21).ToString("yyyy-MM-dd")
        };
    }

    private static List<FlightExploreDestinationDTO> GenerateDestinations(string departureId)
    {
        var destinations = new List<(string id, string name, string country, string code, double lat, double lon, int price, int duration)>
        {
            ("/m/0vzm", "Austin", "United States", "AUS", 30.2672, -97.7431, 189, 240),
            ("/m/030qb", "Los Angeles", "United States", "LAX", 34.0522, -118.2437, 219, 360),
            ("/m/0f2wj", "Miami", "United States", "MIA", 25.7617, -80.1918, 159, 180),
            ("/m/04jpl", "Paris", "France", "CDG", 48.8566, 2.3522, 499, 480),
            ("/m/05qtj", "London", "United Kingdom", "LHR", 51.5074, -0.1278, 549, 450),
            ("/m/0k6bt", "Tokyo", "Japan", "NRT", 35.6762, 139.6503, 899, 840),
            ("/m/06gmr", "Barcelona", "Spain", "BCN", 41.3851, 2.1734, 449, 510),
            ("/m/02_286", "Rome", "Italy", "FCO", 41.9028, 12.4964, 479, 540),
            ("/m/01cx_", "Amsterdam", "Netherlands", "AMS", 52.3676, 4.9041, 429, 420),
            ("/m/0d06m", "Dubai", "United Arab Emirates", "DXB", 25.2048, 55.2708, 699, 720)
        };

        var random = new Random(departureId.GetHashCode()); // Deterministic random based on departure
        var airlines = new[] { "United Airlines", "Delta Air Lines", "American Airlines", "JetBlue Airways", "Southwest Airlines", "Air France", "British Airways", "Lufthansa" };
        var airlineCodes = new[] { "UA", "DL", "AA", "B6", "WN", "AF", "BA", "LH" };

        return destinations.Select((d, index) =>
        {
            var airlineIndex = random.Next(airlines.Length);
            var hotelPrice = random.Next(80, 300);
            var stops = random.Next(0, 3);

            return new FlightExploreDestinationDTO
            {
                DestinationId = d.id,
                Name = d.name,
                Country = d.country,
                GpsCoordinates = new FlightExploreGpsCoordinatesDTO
                {
                    Latitude = d.lat,
                    Longitude = d.lon
                },
                Thumbnail = $"https://lh5.googleusercontent.com/p/mock_{d.name.ToLower().Replace(" ", "_")}_thumbnail.jpg",
                DestinationAirport = new FlightExploreAirportDTO
                {
                    Code = d.code,
                    Name = $"{d.name} International Airport"
                },
                StartDate = DateTime.Now.AddDays(14).ToString("yyyy-MM-dd"),
                EndDate = DateTime.Now.AddDays(21).ToString("yyyy-MM-dd"),
                FlightPrice = d.price + random.Next(-50, 50),
                HotelPrice = hotelPrice,
                FlightDuration = d.duration,
                NumberOfStops = stops,
                Airline = airlines[airlineIndex],
                AirlineCode = airlineCodes[airlineIndex],
                Link = $"https://www.google.com/travel/flights?dep={departureId}&arr={d.code}",
                SerpApiLink = $"https://serpapi.com/search?engine=google_flights&departure_id={departureId}&arrival_id={d.code}"
            };
        }).ToList();
    }

    private void InitializeMockData()
    {
        // Pre-populate common departure airports
        _mockTravelExploreData["departure_id=JFK"] = CreateMockTravelExploreData("JFK");
        _mockTravelExploreData["departure_id=LAX"] = CreateMockTravelExploreData("LAX");
        _mockTravelExploreData["departure_id=ORD"] = CreateMockTravelExploreData("ORD");
        _mockTravelExploreData["departure_id=SFO"] = CreateMockTravelExploreData("SFO");
        _mockTravelExploreData["departure_id=MIA"] = CreateMockTravelExploreData("MIA");
    }
}
