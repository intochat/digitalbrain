using System.Text.Json;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Mocks.Responses.SerpApi.Flights;
using AirportDetail = TripRadar.Server.Application.DTO.Models.AirportDetail;
using AirportInfo = TripRadar.Server.Application.DTO.Models.AirportInfo;
using FlightOption = TripRadar.Server.Application.DTO.Models.FlightOption;
using FlightSegment = TripRadar.Server.Application.DTO.Models.FlightSegment;
using Layover = TripRadar.Server.Application.DTO.Models.Layover;
using SearchMetadata = TripRadar.Server.Application.DTO.Models.SearchMetadata;

namespace TripRadar.Server.Mocks.Builders;

public class SerpApiGoogleFlightResponseBuilder
{
    private readonly ILogger<SerpApiGoogleFlightResponseBuilder> _logger;
    private readonly Dictionary<string, GetFlightResponseDTO> _mockFlightData = new();

    public SerpApiGoogleFlightResponseBuilder(ILogger<SerpApiGoogleFlightResponseBuilder> logger)
    {
        _logger = logger;
        InitializeMockData();
    }

    public GetFlightResponseDTO GetFlightData(string departure, string arrival, string outboundDate,
        string? returnDate = null)
    {
        if (string.Equals(departure, arrival, StringComparison.OrdinalIgnoreCase))
        {
            return CreateMockFlightData(departure, arrival, outboundDate, returnDate);
        }

        var cacheKey = $"departure={departure}&arrival={arrival}&outbound_date={outboundDate}";
        if (!string.IsNullOrEmpty(returnDate))
        {
            cacheKey += $"&return_date={returnDate}";
        }

        if (_mockFlightData.TryGetValue(cacheKey, out var mockData))
        {
            return mockData;
        }

        var routeKey = $"departure={departure}&arrival={arrival}";
        if (_mockFlightData.TryGetValue(routeKey, out var routeFlightData))
        {
            var adjustedData = ModifyDates(routeFlightData, outboundDate);
            _mockFlightData[cacheKey] = adjustedData;

            _logger.LogInformation("Returning adjusted mock data for route {Departure} to {Arrival}", departure,
                arrival);
            return adjustedData;
        }

        _logger.LogInformation("Creating new mock data for route {Departure} to {Arrival}", departure, arrival);
        var defaultData = CreateMockFlightData(departure, arrival, outboundDate, returnDate);
        _mockFlightData[cacheKey] = defaultData;
        AddMockRoute(departure, arrival, defaultData);

        return defaultData;
    }

    private void AddMockRoute(string departure, string arrival, GetFlightResponseDTO getFlightResponseDto)
    {
        _mockFlightData[$"departure={departure}&arrival={arrival}"] = getFlightResponseDto;
    }

    private GetFlightResponseDTO ModifyDates(GetFlightResponseDTO source, string outboundDate)
    {
        var clone = JsonSerializer.Deserialize<GetFlightResponseDTO>(JsonSerializer.Serialize(source));
        if (clone == null)
        {
            return source;
        }

        if (clone.SearchParameters != null)
        {
            clone.SearchParameters.OutboundDate = outboundDate;
        }

        return clone;
    }

    private GetFlightResponseDTO CreateMockFlightData(string departure, string arrival, string outboundDate,
        string? returnDate)
    {
        var searchMetadata = new SearchMetadata
        {
            Id = Guid.NewGuid().ToString(),
            Status = "Success",
            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss UTC"),
            TotalTimeTaken = 3.2
        };

        var searchParameters = new FlightSearchParameters
        {
            Engine = "google_flights",
            DepartureId = departure,
            ArrivalId = arrival,
            OutboundDate = outboundDate,
            Adults = 1,
            Currency = "USD",
            LanguageCode = "en",
            CountryCode = "us"
        };

        var bestFlights = GenerateBestFlights(departure, arrival, outboundDate, returnDate);
        var otherFlights = GenerateOtherFlights(departure, arrival, outboundDate);
        var airports = GenerateAirports(departure, arrival);
        var priceInsights = GeneratePriceInsights();

        return new GetFlightResponseDTO
        {
            SearchMetadata = searchMetadata,
            SearchParameters = searchParameters,
            BestFlights = bestFlights,
            OtherFlights = otherFlights,
            Airports = airports,
            PriceInsights = priceInsights
        };
    }

    private List<FlightOption> GenerateBestFlights(string departure, string arrival, string outboundDate,
        string? returnDate)
    {
        var flights = new List<FlightOption>();

        for (var i = 0; i < Random.Shared.Next(2, 4); i++)
        {
            var basePrice = Random.Shared.Next(300, 800);
            var departureTime = DateTime.Parse(outboundDate).AddHours(Random.Shared.Next(6, 20));
            var duration = Random.Shared.Next(180, 480);

            var flightSegments = new List<FlightSegment>
            {
                new()
                {
                    DepartureAirport =
                        new Airport
                        {
                            Name = GetAirportName(departure),
                            Code = departure,
                            Time = departureTime.ToString("HH:mm")
                        },
                    ArrivalAirport =
                        new Airport
                        {
                            Name = GetAirportName(arrival),
                            Code = arrival,
                            Time = departureTime.AddMinutes(duration).ToString("HH:mm")
                        },
                    Duration = duration,
                    Airline = GetRandomAirline(),
                    FlightNumber = $"AA{Random.Shared.Next(1000, 9999)}",
                    TravelClass = "Economy",
                    Airplane = "Boeing 737",
                    AirlineLogo = "https://www.gstatic.com/flights/airline_logos/70px/AA.png",
                    Legroom = "30 in",
                    Extensions = new List<string>()
                }
            };

            if (!string.IsNullOrEmpty(returnDate))
                basePrice *= 2;

            var isRoundTrip = !string.IsNullOrEmpty(returnDate);
            flights.Add(new FlightOption
            {
                Flights = flightSegments,
                Layovers = new List<Layover>(),
                TotalDuration = duration,
                Price = basePrice + Random.Shared.Next(-50, 100),
                Type = isRoundTrip ? "Round trip" : "One way",
                AirlineLogo = "https://www.gstatic.com/flights/airline_logos/70px/AA.png",
                BookingToken = isRoundTrip ? null : Guid.NewGuid().ToString(),
                DepartureToken = isRoundTrip ? Guid.NewGuid().ToString() : null,
                CarbonEmissions = new CarbonEmissions
                {
                    ThisFlight = Random.Shared.Next(200, 500),
                    TypicalForThisRoute = Random.Shared.Next(250, 550),
                    DifferencePercent = Random.Shared.Next(-20, 20)
                }
            });
        }

        return flights;
    }

    private List<FlightOption> GenerateOtherFlights(string departure, string arrival, string outboundDate)
    {
        var flights = new List<FlightOption>();

        for (var i = 0; i < Random.Shared.Next(3, 6); i++)
        {
            var basePrice = Random.Shared.Next(250, 1200);
            var departureTime = DateTime.Parse(outboundDate).AddHours(Random.Shared.Next(5, 22));
            var duration = Random.Shared.Next(200, 600);

            var flightSegments = new List<FlightSegment>
            {
                new()
                {
                    DepartureAirport =
                        new Airport
                        {
                            Name = GetAirportName(departure),
                            Code = departure,
                            Time = departureTime.ToString("HH:mm")
                        },
                    ArrivalAirport =
                        new Airport
                        {
                            Name = GetAirportName(arrival),
                            Code = arrival,
                            Time = departureTime.AddMinutes(duration).ToString("HH:mm")
                        },
                    Duration = duration,
                    Airline = GetRandomAirline(),
                    FlightNumber = $"DL{Random.Shared.Next(1000, 9999)}",
                    TravelClass = "Economy",
                    Airplane = "Boeing 737",
                    AirlineLogo = "https://www.gstatic.com/flights/airline_logos/70px/DL.png",
                    Legroom = "31 in",
                    Extensions = new List<string>()
                }
            };

            flights.Add(new FlightOption
            {
                Flights = flightSegments,
                Layovers = new List<Layover>(),
                TotalDuration = duration,
                Price = basePrice
            });
        }

        return flights;
    }

    private List<AirportInfo> GenerateAirports(string departure, string arrival)
    {
        return new List<AirportInfo>
        {
            new()
            {
                Departure = new List<AirportDetail>
                {
                    new()
                    {
                        Airport = new AirportIdentifier
                        {
                            Id = departure, Name = GetAirportName(departure)
                        },
                        City = GetAirportCity(departure),
                        Country = "United States",
                        CountryCode = "US"
                    }
                }
            },
            new()
            {
                Arrival = new List<AirportDetail>
                {
                    new()
                    {
                        Airport =
                            new AirportIdentifier { Id = arrival, Name = GetAirportName(arrival) },
                        City = GetAirportCity(arrival),
                        Country = "United States",
                        CountryCode = "US"
                    }
                }
            }
        };
    }

    private FlightPriceInsights GeneratePriceInsights()
    {
        return new FlightPriceInsights
        {
            LowestPrice = Random.Shared.Next(200, 400),
            PriceLevel = "typical",
            TypicalPriceRange = [Random.Shared.Next(200, 300), Random.Shared.Next(400, 600)],
            PriceHistory = GeneratePriceHistory()
        };
    }

    private List<List<object>> GeneratePriceHistory()
    {
        // SerpApi returns price_history as [[unixTimestamp, price], ...]; the real resolver
        // indexes point[0]/point[1] so each inner list must have exactly those two elements.
        var history = new List<List<object>>();
        var basePrice = Random.Shared.Next(300, 500);

        for (var i = 0; i < 30; i++)
        {
            history.Add(new List<object>
            {
                DateTimeOffset.UtcNow.AddDays(-i).ToUnixTimeSeconds(),
                basePrice + Random.Shared.Next(-100, 150)
            });
        }

        return history;
    }

    private static string GetAirportName(string code)
    {
        return code switch
        {
            "JFK" => "John F. Kennedy International Airport",
            "LAX" => "Los Angeles International Airport",
            "ORD" => "O'Hare International Airport",
            "ATL" => "Hartsfield-Jackson Atlanta International Airport",
            "DFW" => "Dallas/Fort Worth International Airport",
            _ => $"{code} Airport"
        };
    }

    private static string GetAirportCity(string code)
    {
        return code switch
        {
            "JFK" => "New York",
            "LAX" => "Los Angeles",
            "ORD" => "Chicago",
            "ATL" => "Atlanta",
            "DFW" => "Dallas",
            _ => "Unknown City"
        };
    }

    private static string GetRandomAirline()
    {
        var airlines = new[]
        {
            "American Airlines", "Delta Air Lines", "United Airlines", "Southwest Airlines", "JetBlue Airways"
        };
        return airlines[Random.Shared.Next(airlines.Length)];
    }

    private void InitializeMockData()
    {
        var popularRoutes = new[] { ("JFK", "LAX"), ("ORD", "DFW"), ("ATL", "LAX") };

        foreach (var (departure, arrival) in popularRoutes)
        {
            var mockData =
                CreateMockFlightData(departure, arrival, DateTime.Now.AddDays(7).ToString("yyyy-MM-dd"), null);
            _mockFlightData[$"departure={departure}&arrival={arrival}"] = mockData;
        }
    }
}
