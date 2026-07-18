using System.Collections;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Contracts.Providers;
using TripRadar.Server.Mocks.Builders;
using TripRadar.Server.Mocks.Constants;
using TripRadar.Server.Mocks.Extensions;

namespace TripRadar.Server.Mocks.Clients;

public class MockSerpApiProvider(
    SerpApiGoogleFlightResponseBuilder flightResponseBuilder,
    SerpApiGoogleHotelResponseBuilder hotelResponseBuilder,
    SerpApiGoogleEventsResponseBuilder eventsResponseBuilder,
    SerpApiGoogleLocalResponseBuilder localResponseBuilder,
    SerpApiGoogleMapsResponseBuilder mapsResponseBuilder,
    SerpApiPlaceReviewsResponseBuilder placeReviewsResponseBuilder,
    SerpApiGoogleTravelExploreResponseBuilder travelExploreResponseBuilder,
    SerpApiTripAdvisorSearchResponseBuilder tripAdvisorSearchResponseBuilder,
    SerpApiTripAdvisorPlaceResponseBuilder tripAdvisorPlaceResponseBuilder,
    ILogger<MockSerpApiProvider> logger)
    : ISerpApiProvider
{
    public bool SimulateEventsServiceFailure { get; set; }
    public bool SimulateFlightsServiceFailure { get; set; }
    public bool SimulateHotelsServiceFailure { get; set; }
    public bool SimulateLocalPlacesServiceFailure { get; set; }
    public bool SimulateMapsServiceFailure { get; set; }
    public bool SimulatePlaceReviewsServiceFailure { get; set; }
    public bool SimulateTravelExploreServiceFailure { get; set; }
    public bool SimulateTripAdvisorSearchServiceFailure { get; set; }
    public bool SimulateTripAdvisorPlaceServiceFailure { get; set; }
    public bool SimulateYelpSearchServiceFailure { get; set; }
    public bool SimulateYelpPlaceServiceFailure { get; set; }
    public bool SimulateYelpReviewsServiceFailure { get; set; }
    public bool SimulateMapsDirectionsServiceFailure { get; set; }
    public bool SimulateOpenTableReviewsServiceFailure { get; set; }
    public bool SimulateYouTubeSearchServiceFailure { get; set; }
    public bool SimulateGoogleLightSearchServiceFailure { get; set; }

    public Task<string?> FindAsync(Hashtable parameters, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var engineName = GetEngineFromParameters(parameters);
            var engine = engineName.ToSerpApiEngine();

            logger.LogInformation("Processing mock request for engine: {Engine} ({Description})",
                engineName, engine.GetDescription());

            var mockResponseJson = engine switch
            {
                SerpApiEngine.GoogleFlights => GetFlightMockResponse(parameters),
                SerpApiEngine.GoogleHotels => GetHotelMockResponse(parameters),
                SerpApiEngine.GoogleEvents => GetEventsMockResponse(parameters),
                SerpApiEngine.GoogleLocal => GetLocationsMockResponse(parameters),
                SerpApiEngine.GoogleMaps => GetMapsMockResponse(parameters),
                SerpApiEngine.PlaceReviews => GetPlaceReviewsMockResponse(parameters),
                SerpApiEngine.GoogleMapsDirections => GetMapsDirectionsMockResponse(parameters),
                SerpApiEngine.GoogleTravelExplore => GetTravelExploreMockResponse(parameters),
                SerpApiEngine.TripadvisorSearch => GetTripAdvisorSearchMockResponse(parameters),
                SerpApiEngine.TripadvisorPlace => GetTripAdvisorPlaceMockResponse(parameters),
                SerpApiEngine.OpenTableReviews => GetOpenTableReviewsMockResponse(parameters),
                SerpApiEngine.YouTubeSearch => GetYouTubeSearchMockResponse(parameters),
                SerpApiEngine.GoogleLightSearch => GetGoogleLightSearchMockResponse(parameters),
                SerpApiEngine.YelpSearch => GetYelpSearchMockResponse(parameters),
                SerpApiEngine.YelpPlace => GetYelpPlaceMockResponse(parameters),
                SerpApiEngine.YelpReviews => GetYelpReviewsMockResponse(parameters),
                SerpApiEngine.Unknown => throw new NotSupportedException(
                    $"The engine '{engineName}' is not supported by the mock provider. " +
                    $"Supported engines are: {string.Join(", ", GetSupportedEngines())}"),
                _ => throw new ArgumentOutOfRangeException(nameof(engine), engine,
                    $"The engine type '{engine}' is not implemented in the mock provider.")
            };

            if (!string.IsNullOrEmpty(mockResponseJson))
            {
                return Task.FromResult<string?>(mockResponseJson);
            }

            logger.LogWarning("Empty mock response generated for engine: {Engine}", engineName);
            return Task.FromResult<string?>(null);
        }
        catch (NotSupportedException ex)
        {
            logger.LogError(ex, "Unsupported engine in mock request: {Message}", ex.Message);
            throw;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            logger.LogError(ex, "Invalid engine type in mock request: {Message}", ex.Message);
            throw;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP request failed for mock provider: {Message}", ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating mock response for parameters: {Parameters}",
                string.Join(", ", parameters.Keys.Cast<object>()));
            return Task.FromResult<string?>(null);
        }
    }

    private static string GetEngineFromParameters(Hashtable parameters)
    {
        return parameters["engine"]?.ToString() ?? "unknown";
    }

    private static IEnumerable<string> GetSupportedEngines()
    {
        return
        [
            SerpApiEngine.GoogleFlights.ToEngineString(),
            SerpApiEngine.GoogleHotels.ToEngineString(),
            SerpApiEngine.GoogleEvents.ToEngineString(),
            SerpApiEngine.GoogleLocal.ToEngineString(),
            SerpApiEngine.GoogleMaps.ToEngineString(),
            SerpApiEngine.PlaceReviews.ToEngineString(),
            SerpApiEngine.GoogleMapsDirections.ToEngineString(),
            SerpApiEngine.GoogleTravelExplore.ToEngineString(),
            SerpApiEngine.TripadvisorSearch.ToEngineString(),
            SerpApiEngine.TripadvisorPlace.ToEngineString(),
            SerpApiEngine.OpenTableReviews.ToEngineString(),
            SerpApiEngine.YouTubeSearch.ToEngineString(),
            SerpApiEngine.GoogleLightSearch.ToEngineString(),
            SerpApiEngine.YelpSearch.ToEngineString(),
            SerpApiEngine.YelpPlace.ToEngineString(),
            SerpApiEngine.YelpReviews.ToEngineString()
        ];
    }

    private string GetFlightMockResponse(Hashtable parameters)
    {
        if (SimulateFlightsServiceFailure)
        {
            throw new HttpRequestException("Flight service unavailable");
        }
        var departure = parameters["departure_id"]?.ToString() ?? "JFK";
        var arrival = parameters["arrival_id"]?.ToString() ?? "LAX";
        var outboundDate = parameters["outbound_date"]?.ToString() ?? DateTime.Now.AddDays(7).ToString("yyyy-MM-dd");
        var returnDate = parameters["return_date"]?.ToString();
        var departureToken = parameters["departure_token"]?.ToString();

        // When departure_token is present, SerpAPI returns return flights (swapped route) with booking_token
        if (!string.IsNullOrEmpty(departureToken) && !string.IsNullOrEmpty(returnDate))
        {
            var returnResponse = flightResponseBuilder.GetFlightData(arrival, departure, returnDate);
            return JsonSerializer.Serialize(returnResponse);
        }

        var flightResponse = flightResponseBuilder.GetFlightData(departure, arrival, outboundDate, returnDate);
        return JsonSerializer.Serialize(flightResponse);
    }

    private string GetHotelMockResponse(Hashtable parameters)
    {
        if (SimulateHotelsServiceFailure)
        {
            throw new HttpRequestException("Hotel service unavailable");
        }
        var location = parameters["q"]?.ToString() ?? "New York";
        var checkInDate = parameters["check_in_date"]?.ToString() ?? DateTime.Now.AddDays(7).ToString("yyyy-MM-dd");
        var checkOutDate = parameters["check_out_date"]?.ToString() ?? DateTime.Now.AddDays(10).ToString("yyyy-MM-dd");
        var adults = int.TryParse(parameters["adults"]?.ToString(), out var parsedAdults) ? parsedAdults : 2;
        var children = int.TryParse(parameters["children"]?.ToString(), out var parsedChildren) ? parsedChildren : 0;
        var currency = parameters["currency"]?.ToString() ?? "USD";
        var languageCode = parameters["hl"]?.ToString() ?? "en";
        var countryCode = parameters["gl"]?.ToString() ?? "us";
        var minPrice = int.TryParse(parameters["price_min"]?.ToString(), out var parsedMinPrice)
            ? parsedMinPrice
            : (int?)null;
        var maxPrice = int.TryParse(parameters["price_max"]?.ToString(), out var parsedMaxPrice)
            ? parsedMaxPrice
            : (int?)null;

        var hotelResponse = hotelResponseBuilder.GetHotelData(
            location,
            checkInDate,
            checkOutDate,
            adults,
            children,
            currency,
            languageCode,
            countryCode,
            minPrice,
            maxPrice);

        // Use default JsonSerializer options which will respect JsonPropertyName attributes
        return JsonSerializer.Serialize(hotelResponse);
    }

    private string GetEventsMockResponse(Hashtable parameters)
    {
        if (SimulateEventsServiceFailure)
        {
            throw new HttpRequestException("Event service unavailable");
        }
        var query = parameters["q"]?.ToString() ?? "events";
        var location = parameters["location"]?.ToString() ?? "New York";
        var date = parameters["date"]?.ToString() ?? DateTime.Now.ToString("yyyy-MM-dd");

        var eventsResponse = eventsResponseBuilder.GetEventsData(query, location, date);
        return JsonSerializer.Serialize(eventsResponse);
    }

    private string GetLocationsMockResponse(Hashtable parameters)
    {
        if (SimulateLocalPlacesServiceFailure)
        {
            throw new HttpRequestException("Local places service unavailable");
        }
        var query = parameters["q"]?.ToString() ?? "restaurants";
        var location = parameters["location"]?.ToString() ?? "New York";
        var type = parameters["type"]?.ToString() ?? "search";

        if (query == "ERROR_TRIGGER_QUERY")
        {
            logger.LogError("Simulating API error for testing purposes");
            throw new InvalidOperationException("Simulated API error for testing purposes");
        }

        var start = 0;
        var pageSize = 20;

        if (parameters["start"] != null && int.TryParse(parameters["start"]?.ToString(), out var parsedStart))
        {
            start = parsedStart;
        }

        if (parameters["num"] != null && int.TryParse(parameters["num"]?.ToString(), out var parsedNum))
        {
            pageSize = Math.Max(1, Math.Min(parsedNum, 50));
        }

        var localResponse = localResponseBuilder.GetLocalData(query, location, type, start, pageSize);
        return JsonSerializer.Serialize(localResponse,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private string GetMapsMockResponse(Hashtable parameters)
    {
        if (SimulateMapsServiceFailure)
        {
            throw new HttpRequestException("Maps service unavailable");
        }
        
        var placeId = parameters["place_id"]?.ToString();
        var type = parameters["type"]?.ToString();
        var query = parameters["q"]?.ToString();

        if (type == "search" && !string.IsNullOrEmpty(query))
        {
            var location = parameters["ll"]?.ToString() ?? "New York";
            var start = 0;
            var pageSize = 20;

            if (parameters["start"] != null && int.TryParse(parameters["start"]?.ToString(), out var parsedStart))
            {
                start = parsedStart;
            }

            if (parameters["num"] != null && int.TryParse(parameters["num"]?.ToString(), out var parsedNum))
            {
                pageSize = Math.Max(1, Math.Min(parsedNum, 50));
            }

            var localResponse = localResponseBuilder.GetLocalData(query, location, type, start, pageSize);
            return JsonSerializer.Serialize(localResponse,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }

        if (string.IsNullOrEmpty(placeId))
        {
            logger.LogError("No place_id provided for Maps request");
            throw new ArgumentException("place_id is required for Maps requests");
        }

        if (placeId == "invalid_id")
        {
            logger.LogError("Invalid place_id provided: {PlaceId}", placeId);
            throw new ArgumentException($"Invalid place ID '{placeId}' - place not found");
        }

        return mapsResponseBuilder.Build();
    }

    private string GetPlaceReviewsMockResponse(Hashtable parameters)
    {
        if (SimulatePlaceReviewsServiceFailure)
        {
            throw new HttpRequestException("Place reviews service unavailable");
        }
        var placeId = parameters["place_id"]?.ToString();
        var dataId = parameters["data_id"]?.ToString();
        var sortBy = parameters["sort_by"]?.ToString();
        var topicId = parameters["topic_id"]?.ToString();
        var pageSize = 10;

        if (parameters["num"] != null && int.TryParse(parameters["num"]?.ToString(), out var parsedNum))
        {
            pageSize = Math.Max(1, Math.Min(parsedNum, 40));
        }

        var placeReviewsResponse = placeReviewsResponseBuilder.GetPlaceReviews(placeId, dataId, sortBy, topicId, pageSize);
        return JsonSerializer.Serialize(placeReviewsResponse);
    }

    private string GetMapsDirectionsMockResponse(Hashtable parameters)
    {
        if (SimulateMapsDirectionsServiceFailure)
        {
            throw new HttpRequestException("Maps directions service unavailable");
        }

        var startAddress = parameters["start_addr"]?.ToString() ?? "Seattle, WA";
        var endAddress = parameters["end_addr"]?.ToString() ?? "Bellevue, WA";
        var startDataId = parameters["start_data_id"]?.ToString();
        var endDataId = parameters["end_data_id"]?.ToString();
        var startCoords = parameters["start_coords"]?.ToString();
        var endCoords = parameters["end_coords"]?.ToString();
        var travelMode = parameters["travel_mode"] != null && int.TryParse(parameters["travel_mode"]?.ToString(), out var parsedTravelMode)
            ? parsedTravelMode
            : (int?)null;
        var distanceUnit = parameters["distance_unit"] != null && int.TryParse(parameters["distance_unit"]?.ToString(), out var parsedDistanceUnit)
            ? parsedDistanceUnit
            : (int?)null;
        var avoid = parameters["avoid"]?.ToString();
        var prefer = parameters["prefer"]?.ToString();
        var route = parameters["route"] != null && int.TryParse(parameters["route"]?.ToString(), out var parsedRoute)
            ? parsedRoute
            : (int?)null;
        var time = parameters["time"]?.ToString();
        var hl = parameters["hl"]?.ToString();
        var gl = parameters["gl"]?.ToString();
        var output = parameters["output"]?.ToString();
        var jsonRestrictor = parameters["json_restrictor"]?.ToString();

        var response = new
        {
            search_metadata = BuildStandardSearchMetadata(
                "google_maps_directions",
                "google_maps_directions_url",
                $"https://www.google.com/maps/dir/?api=1&origin={Uri.EscapeDataString(startAddress)}&destination={Uri.EscapeDataString(endAddress)}"),
            search_parameters = new
            {
                engine = "google_maps_directions",
                start_addr = startAddress,
                end_addr = endAddress,
                start_data_id = startDataId,
                end_data_id = endDataId,
                start_coords = startCoords,
                end_coords = endCoords,
                travel_mode = travelMode,
                distance_unit = distanceUnit,
                avoid = avoid,
                prefer = prefer,
                route = route,
                time = time,
                hl = hl,
                gl = gl,
                output = output,
                json_restrictor = jsonRestrictor
            },
            places_info = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["address"] = startAddress,
                    ["data_id"] = "mock-start-data-id",
                    ["gps_coordinates"] = new Dictionary<string, object>
                    {
                        ["latitude"] = 47.6062,
                        ["longitude"] = -122.3321
                    }
                },
                new()
                {
                    ["address"] = endAddress,
                    ["data_id"] = "mock-end-data-id",
                    ["gps_coordinates"] = new Dictionary<string, object>
                    {
                        ["latitude"] = 47.6101,
                        ["longitude"] = -122.2015
                    }
                }
            },
            directions = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["travel_mode"] = "Driving",
                    ["via"] = "I-90 E",
                    ["distance"] = 16756,
                    ["duration"] = 942,
                    ["formatted_distance"] = "10.4 miles",
                    ["formatted_duration"] = "16 min"
                }
            },
            durations = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["travel_mode"] = "Driving",
                    ["duration"] = 942,
                    ["formatted_duration"] = "16 min"
                },
                new()
                {
                    ["travel_mode"] = "Transit",
                    ["duration"] = 1920,
                    ["formatted_duration"] = "32 min"
                }
            },
            error = (string?)null
        };

        return JsonSerializer.Serialize(response);
    }

    private string GetOpenTableReviewsMockResponse(Hashtable parameters)
    {
        if (SimulateOpenTableReviewsServiceFailure)
        {
            throw new HttpRequestException("OpenTable reviews service unavailable");
        }

        var rid = parameters["rid"]?.ToString();
        if (string.IsNullOrWhiteSpace(rid))
        {
            throw new ArgumentException("rid is required for OpenTable reviews requests");
        }

        var openTableDomain = parameters["open_table_domain"]?.ToString() ?? "opentable.com";
        var page = parameters["page"]?.ToString() ?? "1";

        var response = new
        {
            search_metadata = BuildStandardSearchMetadata(
                "open_table_reviews",
                "open_table_reviews_url",
                $"https://www.{openTableDomain}/{rid.TrimStart('/')}"),
            search_parameters = new
            {
                engine = "open_table_reviews",
                rid = rid,
                open_table_domain = openTableDomain,
                page = page
            },
            search_information = new
            {
                page = int.TryParse(page, out var parsedPage) ? parsedPage : 1,
                total_pages = 1
            },
            reviews_summary = new
            {
                reviews_count = 2,
                ratings_count = 2,
                ratings_summary = new
                {
                    overall = 4.6,
                    food = 4.5,
                    service = 4.7,
                    ambience = 4.8,
                    value = 4.3,
                    noise = "Moderate"
                },
                ratings = new List<Dictionary<string, object>>
                {
                    new() { ["stars"] = 5, ["count"] = 1 },
                    new() { ["stars"] = 4, ["count"] = 1 }
                },
                ai_summary = "Mock OpenTable reviews summary generated by mock provider."
            },
            awards = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["location"] = "New York City",
                    ["name"] = "Diners' Choice"
                }
            },
            reviews = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["id"] = "mock-opentable-review-1",
                    ["content"] = "Great ambience and solid service.",
                    ["submitted_at"] = DateTime.UtcNow.ToString("o"),
                    ["dined_at"] = DateTime.UtcNow.AddDays(-2).ToString("o"),
                    ["rating"] = new Dictionary<string, object>
                    {
                        ["overall"] = 5,
                        ["food"] = 5,
                        ["service"] = 5,
                        ["ambience"] = 5,
                        ["value"] = 4,
                        ["noise"] = "Moderate"
                    },
                    ["user"] = new Dictionary<string, object>
                    {
                        ["name"] = "Mock Guest",
                        ["number_of_reviews"] = 12,
                        ["location"] = "Seattle",
                        ["vip"] = true
                    }
                },
                new()
                {
                    ["id"] = "mock-opentable-review-2",
                    ["content"] = "Good food, quick seating.",
                    ["submitted_at"] = DateTime.UtcNow.AddDays(-1).ToString("o"),
                    ["dined_at"] = DateTime.UtcNow.AddDays(-3).ToString("o"),
                    ["rating"] = new Dictionary<string, object>
                    {
                        ["overall"] = 4,
                        ["food"] = 4,
                        ["service"] = 4,
                        ["ambience"] = 5,
                        ["value"] = 4,
                        ["noise"] = "Quiet"
                    },
                    ["user"] = new Dictionary<string, object>
                    {
                        ["name"] = "Mock Visitor",
                        ["number_of_reviews"] = 3,
                        ["location"] = "Portland"
                    }
                }
            },
            serpapi_pagination = new
            {
                next = $"https://serpapi.com/search.json?engine=open_table_reviews&open_table_domain={openTableDomain}&page=2&rid={Uri.EscapeDataString(rid)}"
            },
            error = (string?)null
        };

        return JsonSerializer.Serialize(response);
    }

    private string GetYouTubeSearchMockResponse(Hashtable parameters)
    {
        if (SimulateYouTubeSearchServiceFailure)
        {
            throw new HttpRequestException("YouTube search service unavailable");
        }

        var searchQuery = parameters["search_query"]?.ToString() ?? "travel guide";
        var gl = parameters["gl"]?.ToString();
        var hl = parameters["hl"]?.ToString();
        var sp = parameters["sp"]?.ToString();
        var output = parameters["output"]?.ToString();
        var jsonRestrictor = parameters["json_restrictor"]?.ToString();

        var response = new
        {
            search_metadata = BuildStandardSearchMetadata(
                "youtube",
                "youtube_url",
                $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(searchQuery)}"),
            search_parameters = new
            {
                engine = "youtube",
                search_query = searchQuery,
                gl = gl,
                hl = hl,
                sp = sp,
                output = output,
                json_restrictor = jsonRestrictor
            },
            search_information = new
            {
                total_results = 2,
                video_results_state = "Results for exact spelling"
            },
            video_results = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["position_on_page"] = 1,
                    ["title"] = "Mock Seattle Travel Guide",
                    ["link"] = "https://www.youtube.com/watch?v=mock_video_1",
                    ["channel"] = new Dictionary<string, object>
                    {
                        ["name"] = "Mock Travel Channel"
                    },
                    ["published_date"] = "1 year ago",
                    ["views"] = 12345,
                    ["length"] = "10:15"
                },
                new()
                {
                    ["position_on_page"] = 2,
                    ["title"] = "Top Places to Visit in Seattle",
                    ["link"] = "https://www.youtube.com/watch?v=mock_video_2",
                    ["channel"] = new Dictionary<string, object>
                    {
                        ["name"] = "Mock Explorer"
                    },
                    ["published_date"] = "3 months ago",
                    ["views"] = 6789,
                    ["length"] = "8:42"
                }
            },
            playlist_results = new List<Dictionary<string, object>>(),
            channel_results = new List<Dictionary<string, object>>(),
            movie_results = new List<Dictionary<string, object>>(),
            ads_results = new List<Dictionary<string, object>>(),
            pagination = new Dictionary<string, object>
            {
                ["current"] = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(searchQuery)}"
            },
            serpapi_pagination = new Dictionary<string, object>
            {
                ["current"] = $"https://serpapi.com/search.json?engine=youtube&search_query={Uri.EscapeDataString(searchQuery)}"
            }
        };

        return JsonSerializer.Serialize(response);
    }

    private string GetGoogleLightSearchMockResponse(Hashtable parameters)
    {
        if (SimulateGoogleLightSearchServiceFailure)
        {
            throw new HttpRequestException("Google Light search service unavailable");
        }

        var query = parameters["q"]?.ToString() ?? "travel";
        var location = parameters["location"]?.ToString();
        var googleDomain = parameters["google_domain"]?.ToString() ?? "google.com";
        var gl = parameters["gl"]?.ToString();
        var hl = parameters["hl"]?.ToString();
        var lr = parameters["lr"]?.ToString();
        var start = parameters["start"] != null && int.TryParse(parameters["start"]?.ToString(), out var parsedStart)
            ? parsedStart
            : 0;
        var device = parameters["device"]?.ToString() ?? "desktop";

        var response = new
        {
            search_metadata = BuildStandardSearchMetadata(
                "google_light",
                "google_light_url",
                $"https://www.google.com/search?q={Uri.EscapeDataString(query)}&gbv=1"),
            search_parameters = new
            {
                engine = "google_light",
                q = query,
                location = location,
                google_domain = googleDomain,
                gl = gl,
                hl = hl,
                lr = lr,
                start = start,
                device = device,
                nfpr = TryParseBoolean(parameters["nfpr"]),
                filter = TryParseBoolean(parameters["filter"]),
                no_cache = TryParseBoolean(parameters["no_cache"]),
                async = TryParseBoolean(parameters["async"]),
                zero_trace = TryParseBoolean(parameters["zero_trace"]),
                output = parameters["output"]?.ToString(),
                json_restrictor = parameters["json_restrictor"]?.ToString()
            },
            search_information = new
            {
                total_results = 1
            },
            organic_results = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["position"] = 1,
                    ["title"] = "Mock Google Light Result",
                    ["link"] = "https://example.com/mock-google-light",
                    ["displayed_link"] = "example.com",
                    ["snippet"] = $"Mock result for query '{query}'."
                }
            },
            related_searches = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["query"] = $"{query} near me",
                    ["link"] = $"https://www.google.com/search?q={Uri.EscapeDataString(query + " near me")}"
                }
            },
            pagination = new Dictionary<string, object>
            {
                ["current"] = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}&gbv=1"
            },
            serpapi_pagination = new Dictionary<string, object>
            {
                ["current"] = $"https://serpapi.com/search.json?engine=google_light&q={Uri.EscapeDataString(query)}"
            }
        };

        return JsonSerializer.Serialize(response);
    }

    private string GetTravelExploreMockResponse(Hashtable parameters)
    {
        if (SimulateTravelExploreServiceFailure)
        {
            throw new HttpRequestException("Travel explore service unavailable");
        }

        var departureId = parameters["departure_id"]?.ToString() ?? "JFK";
        var arrivalId = parameters["arrival_id"]?.ToString();
        var arrivalAreaId = parameters["arrival_area_id"]?.ToString();

        var travelExploreResponse = travelExploreResponseBuilder.GetTravelExploreData(departureId, arrivalId, arrivalAreaId);
        return JsonSerializer.Serialize(travelExploreResponse);
    }

    private string GetTripAdvisorSearchMockResponse(Hashtable parameters)
    {
        if (SimulateTripAdvisorSearchServiceFailure)
        {
            throw new HttpRequestException("TripAdvisor search service unavailable");
        }

        var query = parameters["q"]?.ToString() ?? "restaurants";
        var tripadvisorDomain = parameters["tripadvisor_domain"]?.ToString();
        var ssrc = parameters["ssrc"]?.ToString();
        var offset = parameters["offset"] != null && int.TryParse(parameters["offset"]?.ToString(), out var parsedOffset)
            ? parsedOffset
            : (int?)null;
        var limit = parameters["limit"] != null && int.TryParse(parameters["limit"]?.ToString(), out var parsedLimit)
            ? parsedLimit
            : (int?)null;
        var lat = parameters["lat"] != null && double.TryParse(parameters["lat"]?.ToString(), out var parsedLat)
            ? parsedLat
            : (double?)null;
        var lon = parameters["lon"] != null && double.TryParse(parameters["lon"]?.ToString(), out var parsedLon)
            ? parsedLon
            : (double?)null;

        return tripAdvisorSearchResponseBuilder.BuildJson(query, tripadvisorDomain, ssrc, offset, limit, lat, lon);
    }

    private string GetTripAdvisorPlaceMockResponse(Hashtable parameters)
    {
        if (SimulateTripAdvisorPlaceServiceFailure)
        {
            throw new HttpRequestException("TripAdvisor place service unavailable");
        }

        var placeId = parameters["place_id"]?.ToString();
        if (string.IsNullOrWhiteSpace(placeId))
        {
            throw new ArgumentException("place_id is required for TripAdvisor place requests");
        }

        var tripadvisorDomain = parameters["tripadvisor_domain"]?.ToString();

        return tripAdvisorPlaceResponseBuilder.BuildJson(placeId, tripadvisorDomain);
    }

    private string GetYelpSearchMockResponse(Hashtable parameters)
    {
        if (SimulateYelpSearchServiceFailure)
        {
            throw new HttpRequestException("Yelp search service unavailable");
        }

        var findDesc = parameters["find_desc"]?.ToString() ?? "restaurants";
        var findLoc = parameters["find_loc"]?.ToString() ?? "New York, NY";
        var yelpDomain = parameters["yelp_domain"]?.ToString() ?? "www.yelp.com";
        var sortBy = parameters["sortby"]?.ToString();
        var attrs = parameters["attrs"]?.ToString();
        var cflt = parameters["cflt"]?.ToString();
        var start = parameters["start"] != null && int.TryParse(parameters["start"]?.ToString(), out var parsedStart)
            ? parsedStart
            : 0;

        var response = new
        {
            search_metadata = BuildYelpSearchMetadata("yelp", yelpDomain),
            search_parameters = new
            {
                engine = "yelp",
                find_desc = findDesc,
                find_loc = findLoc,
                yelp_domain = yelpDomain,
                sortby = sortBy,
                attrs = attrs,
                cflt = cflt,
                start = start
            },
            search_information = new { total_results = 1 },
            filters = new Dictionary<string, object>(),
            ads_results = new List<Dictionary<string, object>>(),
            organic_results = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["position"] = 1,
                    ["title"] = "Mock Yelp Listing",
                    ["place_id"] = "mock-yelp-place-1",
                    ["rating"] = 4.6,
                    ["reviews"] = 128,
                    ["price"] = "$$",
                    ["address"] = "123 Mock Street, New York, NY",
                    ["phone"] = "+1 555-000-0000"
                }
            },
            pagination = new Dictionary<string, object>(),
            serpapi_pagination = new Dictionary<string, object>()
        };

        return JsonSerializer.Serialize(response);
    }

    private string GetYelpPlaceMockResponse(Hashtable parameters)
    {
        if (SimulateYelpPlaceServiceFailure)
        {
            throw new HttpRequestException("Yelp place service unavailable");
        }

        var placeId = parameters["place_id"]?.ToString();
        if (string.IsNullOrWhiteSpace(placeId))
        {
            throw new ArgumentException("place_id is required for Yelp place requests");
        }

        var yelpDomain = parameters["yelp_domain"]?.ToString() ?? "www.yelp.com";
        var fullMenu = TryParseBoolean(parameters["full_menu"]);
        var menuName = parameters["menu_name"]?.ToString();

        var response = new
        {
            search_metadata = BuildYelpSearchMetadata("yelp_place", yelpDomain),
            search_parameters = new
            {
                engine = "yelp_place",
                place_id = placeId,
                yelp_domain = yelpDomain,
                full_menu = fullMenu,
                menu_name = menuName
            },
            search_information = new { total_results = 1 },
            place_results = new Dictionary<string, object>
            {
                ["place_id"] = placeId,
                ["name"] = "Mock Yelp Place",
                ["rating"] = 4.7,
                ["reviews"] = 241,
                ["price"] = "$$",
                ["address"] = "456 Sample Avenue, New York, NY",
                ["phone"] = "+1 555-111-2222",
                ["website"] = "https://example.com/mock-yelp-place"
            },
            full_menu_results = fullMenu == true
                ? new Dictionary<string, object>
                {
                    ["menu_name"] = string.IsNullOrWhiteSpace(menuName) ? "Main Menu" : menuName,
                    ["sections"] = new List<Dictionary<string, object>>
                    {
                        new()
                        {
                            ["name"] = "Mock Starters",
                            ["items"] = new List<Dictionary<string, object>>
                            {
                                new()
                                {
                                    ["name"] = "Mock Soup",
                                    ["price"] = "$9.00",
                                    ["description"] = "Generated by mock provider"
                                }
                            }
                        }
                    }
                }
                : null,
            error = (string?)null
        };

        return JsonSerializer.Serialize(response);
    }

    private string GetYelpReviewsMockResponse(Hashtable parameters)
    {
        if (SimulateYelpReviewsServiceFailure)
        {
            throw new HttpRequestException("Yelp reviews service unavailable");
        }

        var placeId = parameters["place_id"]?.ToString();
        if (string.IsNullOrWhiteSpace(placeId))
        {
            throw new ArgumentException("place_id is required for Yelp reviews requests");
        }

        var yelpDomain = parameters["yelp_domain"]?.ToString() ?? "www.yelp.com";
        var language = parameters["language"]?.ToString() ?? "en";
        var sortBy = parameters["sortby"]?.ToString();
        var rating = parameters["rating"] != null && int.TryParse(parameters["rating"]?.ToString(), out var parsedRating)
            ? parsedRating
            : (int?)null;
        var notRecommended = TryParseBoolean(parameters["not_recommended"]);
        var start = parameters["start"] != null && int.TryParse(parameters["start"]?.ToString(), out var parsedStart)
            ? parsedStart
            : 0;
        var num = parameters["num"] != null && int.TryParse(parameters["num"]?.ToString(), out var parsedNum)
            ? Math.Max(1, Math.Min(parsedNum, 40))
            : 10;
        var q = parameters["q"]?.ToString();

        var response = new
        {
            search_metadata = BuildYelpSearchMetadata("yelp_reviews", yelpDomain),
            search_parameters = new
            {
                engine = "yelp_reviews",
                place_id = placeId,
                yelp_domain = yelpDomain,
                language = language,
                sortby = sortBy,
                rating = rating,
                not_recommended = notRecommended,
                start = start,
                num = num,
                q = q
            },
            search_information = new { total_results = 2 },
            review_languages = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["language_code"] = language,
                    ["language_name"] = language == "en" ? "English" : "Unknown"
                }
            },
            reviews = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["user_name"] = "Mock User 1",
                    ["rating"] = 5,
                    ["date"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    ["snippet"] = "Great place, generated by mock provider."
                },
                new()
                {
                    ["user_name"] = "Mock User 2",
                    ["rating"] = 4,
                    ["date"] = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd"),
                    ["snippet"] = "Good food and atmosphere."
                }
            },
            serpapi_pagination = new Dictionary<string, object>(),
            error = (string?)null
        };

        return JsonSerializer.Serialize(response);
    }

    private static Dictionary<string, object?> BuildYelpSearchMetadata(string engine, string yelpDomain)
    {
        var now = DateTime.UtcNow.ToString("o");

        return new Dictionary<string, object?>
        {
            ["id"] = $"mock-{engine}-{Guid.NewGuid():N}",
            ["status"] = "Success",
            ["json_endpoint"] = "https://serpapi.mock.local/search.json",
            ["created_at"] = now,
            ["processed_at"] = now,
            ["raw_html_file"] = "https://serpapi.mock.local/raw.html",
            ["prettify_html_file"] = "https://serpapi.mock.local/prettify.html",
            ["yelp_url"] = $"https://{yelpDomain}/search",
            ["yelp_place_url"] = $"https://{yelpDomain}/biz/mock-yelp-place-1",
            ["yelp_reviews_url"] = $"https://{yelpDomain}/biz/mock-yelp-place-1/reviews",
            ["total_time_taken"] = 0.15
        };
    }

    private static Dictionary<string, object?> BuildStandardSearchMetadata(
        string engine,
        string? engineUrlFieldName = null,
        string? engineUrl = null)
    {
        var now = DateTime.UtcNow.ToString("o");
        var metadata = new Dictionary<string, object?>
        {
            ["id"] = $"mock-{engine}-{Guid.NewGuid():N}",
            ["status"] = "Success",
            ["json_endpoint"] = "https://serpapi.mock.local/search.json",
            ["created_at"] = now,
            ["processed_at"] = now,
            ["raw_html_file"] = "https://serpapi.mock.local/raw.html",
            ["prettify_html_file"] = "https://serpapi.mock.local/prettify.html",
            ["total_time_taken"] = 0.12
        };

        if (!string.IsNullOrWhiteSpace(engineUrlFieldName) && !string.IsNullOrWhiteSpace(engineUrl))
        {
            metadata[engineUrlFieldName] = engineUrl;
        }

        return metadata;
    }

    private static bool? TryParseBoolean(object? value)
    {
        if (value == null)
        {
            return null;
        }

        if (value is bool booleanValue)
        {
            return booleanValue;
        }

        var rawValue = value.ToString();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (bool.TryParse(rawValue, out var parsed))
        {
            return parsed;
        }

        return rawValue switch
        {
            "1" => true,
            "0" => false,
            _ => null
        };
    }
}
