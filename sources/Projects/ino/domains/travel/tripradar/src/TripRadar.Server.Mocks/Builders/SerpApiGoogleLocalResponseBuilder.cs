using System.Text.Json;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Mocks.Responses.SerpApi.Local;

namespace TripRadar.Server.Mocks.Builders;

public class SerpApiGoogleLocalResponseBuilder
{
    private readonly ILogger<SerpApiGoogleLocalResponseBuilder> _logger;
    private readonly Dictionary<string, GoogleLocalResponse> _mockLocalData = new();

    public SerpApiGoogleLocalResponseBuilder(ILogger<SerpApiGoogleLocalResponseBuilder> logger)
    {
        _logger = logger;
        InitializeMockData();
    }

    public GoogleLocalResponse GetLocalData(string query, string location, string type = "search", int start = 0,
        int num = 20)
    {
        _logger.LogWarning(
            "DEBUG: GetLocalData called with query='{Query}', location='{Location}', type='{Type}', start={Start}, num={Num}",
            query, location, type, start, num);

        var cacheKey = $"q={query}&location={location}&type={type}";
        _logger.LogInformation("Cache key: {CacheKey}", cacheKey);

        GoogleLocalResponse baseData;
        if (_mockLocalData.TryGetValue(cacheKey, out var mockData))
        {
            _logger.LogInformation("Found cached data for key: {CacheKey}", cacheKey);
            baseData = mockData;
        }
        else
        {
            var locationKey = $"location={location}";
            _logger.LogInformation("No exact cache match, trying location key: {LocationKey}", locationKey);
            if (_mockLocalData.TryGetValue(locationKey, out var locationLocalData))
            {
                _logger.LogInformation("Found location data, modifying for query: {Query}", query);
                baseData = ModifyQueryResults(locationLocalData, query, type);
                _mockLocalData[cacheKey] = baseData;
            }
            else
            {
                _logger.LogInformation("Creating new mock local data for query {Query} in {Location}", query, location);
                baseData = CreateMockLocalData(query, location, type);
                _mockLocalData[cacheKey] = baseData;
                AddMockLocation(location, baseData);
            }
        }

        // Apply pagination to the results
        _logger.LogWarning(
            "DEBUG: About to apply pagination - start={Start}, num={Num}, original results count={OriginalCount}",
            start, num, baseData.LocalResults?.Count ?? 0);
        var paginatedData = ApplyPagination(baseData, start, num);
        _logger.LogWarning("DEBUG: Pagination complete - final results count={FinalCount}",
            paginatedData.LocalResults?.Count ?? 0);

        return paginatedData;
    }

    private void AddMockLocation(string location, GoogleLocalResponse googleLocalResponse)
    {
        _mockLocalData[$"location={location}"] = googleLocalResponse;
    }

    private GoogleLocalResponse ApplyPagination(GoogleLocalResponse response, int start, int num)
    {
        Console.WriteLine(
            $"DEBUG: ApplyPagination called with start={start}, num={num}, original results count={response.LocalResults?.Count ?? 0}");

        if (response.LocalResults == null || !response.LocalResults.Any())
        {
            Console.WriteLine("DEBUG: No local results to paginate");
            return response;
        }

        var paginatedResults = response.LocalResults
            .Skip(start)
            .Take(num)
            .ToList();

        Console.WriteLine(
            $"DEBUG: After pagination - returning {paginatedResults.Count} results (start={start}, num={num})");

        var paginatedResponse = new GoogleLocalResponse
        {
            SearchMetadata = response.SearchMetadata,
            SearchParameters = response.SearchParameters,
            LocalResults = paginatedResults,
            PlaceResults = response.PlaceResults,
            Pagination = new Pagination
            {
                Current = start / Math.Max(num, 1) + 1,
                Next = start + num < (response.LocalResults?.Count ?? 0)
                    ? $"https://serpapi.com/search?start={start + num}"
                    : null
            }
        };

        Console.WriteLine($"DEBUG: Final response has {paginatedResponse.LocalResults?.Count ?? 0} results");
        return paginatedResponse;
    }

    private GoogleLocalResponse ModifyQueryResults(GoogleLocalResponse source, string query, string type)
    {
        var clone = JsonSerializer.Deserialize<GoogleLocalResponse>(JsonSerializer.Serialize(source));
        if (clone == null)
        {
            return source;
        }

        if (clone.SearchParameters != null)
        {
            clone.SearchParameters.Query = query;
            clone.SearchParameters.Type = type;
        }

        if (clone.LocalResults == null)
        {
            return clone;
        }

        foreach (var result in clone.LocalResults)
        {
            if (result.Title != null && query.Contains("restaurant", StringComparison.OrdinalIgnoreCase))
            {
                result.Title = result.Title.Replace("Store", "Restaurant").Replace("Shop", "Restaurant");
                result.Type = "Restaurant";
            }
            else if (result.Title != null && query.Contains("hotel", StringComparison.OrdinalIgnoreCase))
            {
                result.Title = result.Title.Replace("Store", "Hotel").Replace("Shop", "Hotel");
                result.Type = "Lodging";
            }
        }

        return clone;
    }

    private GoogleLocalResponse CreateMockLocalData(string query, string location, string type)
    {
        var searchMetadata = new SearchMetadata
        {
            Id = Guid.NewGuid().ToString(),
            Status = "Success",
            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss UTC"),
            ProcessedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss UTC"),
            GoogleUrl = $"https://www.google.com/search?q={query}+{location}&type={type}",
            RawHtmlFile = $"https://serpapi.com/searches/{Guid.NewGuid()}/download?html=true",
            TotalTimeTaken = 1.85
        };

        var searchParameters = new SearchParameters
        {
            Engine = "google_local",
            Query = query,
            Location = location,
            Type = type,
            Hl = "en",
            Gl = "us"
        };

        var localResults = GenerateLocalResults(query, location);

        var pagination = new Pagination
        {
            Current = 1, Next = "https://serpapi.com/searches/local_next_page", OtherPages = new { }
        };

        return new GoogleLocalResponse
        {
            SearchMetadata = searchMetadata,
            SearchParameters = searchParameters,
            LocalResults = localResults,
            Pagination = pagination
        };
    }

    private List<LocalResult> GenerateLocalResults(string query, string location)
    {
        var random = new Random();
        var businessTypes = GetBusinessTypes(query);
        var addresses = GenerateAddresses(location, random);

        return Enumerable.Range(1, 50)
            .Select(i => new LocalResult
            {
                Position = i,
                Title = GenerateBusinessName(businessTypes.primaryType, location, i),
                PlaceId = GeneratePlaceId(),
                DataId = $"0x{random.Next(1000000, 9999999):X}:{random.Next(1000000, 9999999):X}",
                GpsCoordinates = GenerateCoordinates(location, random),
                Rating = Math.Round(3.5 + random.NextDouble() * 1.5, 1),
                Reviews = random.Next(50, 1000),
                Price = businessTypes.hasPrice ? GeneratePriceRange(random) : null,
                Type = businessTypes.primaryType,
                Types = businessTypes.types,
                Address = addresses[i % addresses.Count],
                OpenState = random.NextDouble() > 0.2 ? "Open" : "Closed",
                Hours = "9:00 AM - 6:00 PM",
                OperatingHours = GenerateOperatingHours(),
                Phone = GeneratePhoneNumber(random),
                Website =
                    $"https://www.{GenerateBusinessName(businessTypes.primaryType, location, i).Replace(" ", "").ToLower()}.com",
                Description =
                    $"Quality {businessTypes.primaryType.ToLower()} service in {location}. We provide excellent customer experience.",
                Thumbnail = $"https://via.placeholder.com/150x150?text={businessTypes.primaryType}+{i}"
            })
            .ToList();
    }

    private (string primaryType, List<string> types, bool hasPrice) GetBusinessTypes(string query)
    {
        var queryLower = query.ToLower();

        if (queryLower.Contains("restaurant") || queryLower.Contains("food") || queryLower.Contains("dining"))
        {
            return ("Restaurant", new List<string> { "restaurant", "food", "point_of_interest", "establishment" },
                true);
        }

        if (queryLower.Contains("hotel") || queryLower.Contains("accommodation") || queryLower.Contains("lodge"))
        {
            return ("Lodging", new List<string> { "lodging", "point_of_interest", "establishment" }, true);
        }

        if (queryLower.Contains("shop") || queryLower.Contains("store") || queryLower.Contains("retail"))
        {
            return ("Store", new List<string> { "store", "point_of_interest", "establishment" }, false);
        }

        if (queryLower.Contains("hospital") || queryLower.Contains("clinic") || queryLower.Contains("medical"))
        {
            return ("Hospital", new List<string> { "hospital", "health", "point_of_interest", "establishment" }, false);
        }

        if (queryLower.Contains("gas") || queryLower.Contains("fuel") || queryLower.Contains("station"))
        {
            return ("Gas Station", new List<string> { "gas_station", "point_of_interest", "establishment" }, false);
        }

        return ("Business", new List<string> { "establishment", "point_of_interest" }, false);
    }

    private string GenerateBusinessName(string type, string location, int index)
    {
        var prefixes = type switch
        {
            "Restaurant" => new[] { "The", "Golden", "Royal", "Mama's", "Tony's", "Casa", "Bistro" },
            "Lodging" => new[] { "Grand", "Royal", "Best", "Comfort", "Holiday", "Luxury", "Downtown" },
            "Store" => new[] { "Super", "Quick", "Corner", "Main Street", "Local", "Express", "City" },
            "Hospital" => new[] { "General", "Memorial", "Regional", "Central", "Community", "St.", "Medical" },
            "Gas Station" => new[] { "Shell", "BP", "Exxon", "Speedway", "Quick", "Express", "Local" },
            _ => new[] { "Quality", "Professional", "Premier", "Elite", "Expert", "Trusted", "Local" }
        };

        var suffixes = type switch
        {
            "Restaurant" => new[] { "Grill", "Kitchen", "Cafe", "Diner", "House", "Place", "Restaurant" },
            "Lodging" => new[] { "Hotel", "Inn", "Lodge", "Suites", "Resort", "Motel", "Plaza" },
            "Store" => new[] { "Market", "Store", "Shop", "Mart", "Plaza", "Center", "Outlet" },
            "Hospital" => new[] { "Hospital", "Medical Center", "Clinic", "Health Center", "Medical", "Care Center" },
            "Gas Station" => new[] { "Station", "Gas", "Fuel", "Stop", "Express", "Quick Stop" },
            _ => new[] { "Services", "Solutions", "Group", "Company", "Associates", "Professionals" }
        };

        var random = new Random(index);
        var prefix = prefixes[random.Next(prefixes.Length)];
        var suffix = suffixes[random.Next(suffixes.Length)];

        return $"{prefix} {location} {suffix}";
    }

    private List<string> GenerateAddresses(string location, Random random)
    {
        var streets = new[]
        {
            "Main St", "First Ave", "Oak St", "Park Ave", "Broadway", "Center St", "Elm St", "Washington St"
        };

        return Enumerable.Range(1, 10)
            .Select(_ => $"{random.Next(100, 9999)} {streets[random.Next(streets.Length)]}, {location}")
            .ToList();
    }

    private GpsCoordinates GenerateCoordinates(string location, Random random)
    {
        var baseCoordinates = location.ToLower() switch
        {
            var l when l.Contains("new york") => (40.7128, -74.0060),
            var l when l.Contains("los angeles") => (34.0522, -118.2437),
            var l when l.Contains("chicago") => (41.8781, -87.6298),
            var l when l.Contains("houston") => (29.7604, -95.3698),
            var l when l.Contains("phoenix") => (33.4484, -112.0740),
            var l when l.Contains("philadelphia") => (39.9526, -75.1652),
            var l when l.Contains("san antonio") => (29.4241, -98.4936),
            var l when l.Contains("san diego") => (32.7157, -117.1611),
            var l when l.Contains("dallas") => (32.7767, -96.7970),
            var l when l.Contains("paris") => (48.8566, 2.3522),
            _ => (39.8283, -98.5795)
        };

        var latVariation = (random.NextDouble() - 0.5) * 0.1;
        var lngVariation = (random.NextDouble() - 0.5) * 0.1;

        return new GpsCoordinates
        {
            Latitude = Math.Round(baseCoordinates.Item1 + latVariation, 6),
            Longitude = Math.Round(baseCoordinates.Item2 + lngVariation, 6)
        };
    }

    private string GeneratePriceRange(Random random)
    {
        var priceRanges = new[] { "$", "$$", "$$$", "$$$$" };
        return priceRanges[random.Next(priceRanges.Length)];
    }

    private OperatingHours GenerateOperatingHours()
    {
        return new OperatingHours
        {
            Monday = "9:00 AM - 6:00 PM",
            Tuesday = "9:00 AM - 6:00 PM",
            Wednesday = "9:00 AM - 6:00 PM",
            Thursday = "9:00 AM - 6:00 PM",
            Friday = "9:00 AM - 8:00 PM",
            Saturday = "10:00 AM - 8:00 PM",
            Sunday = "11:00 AM - 5:00 PM"
        };
    }

    private string GeneratePhoneNumber(Random random)
    {
        var areaCode = random.Next(200, 999);
        var exchange = random.Next(200, 999);
        var number = random.Next(1000, 9999);
        return $"({areaCode}) {exchange}-{number}";
    }

    private string GeneratePlaceId()
    {
        var guidString = Guid.NewGuid().ToString("N");
        var safeLength = Math.Min(16, guidString.Length);
        return $"ChIJ{guidString.Substring(0, safeLength)}";
    }

    private void InitializeMockData()
    {
        var popularSearches = new Dictionary<string, (string query, string type)>
        {
            ["New York"] = ("restaurants", "search"),
            ["Los Angeles"] = ("hotels", "search"),
            ["Chicago"] = ("coffee shops", "search"),
            ["Houston"] = ("gas stations", "search"),
            ["Phoenix"] = ("shopping centers", "search"),
            ["Philadelphia"] = ("hospitals", "search"),
            ["San Diego"] = ("attractions", "search"),
            ["Dallas"] = ("restaurants", "search"),
            ["Paris, France"] = ("restaurants", "search")
        };

        foreach (var (location, (query, type)) in popularSearches)
        {
            var mockData = CreateMockLocalData(query, location, type);
            _mockLocalData[$"location={location}"] = mockData;
        }

        _logger.LogInformation("SerpApiGoogleLocalResponseBuilder initialized with {Count} popular locations",
            popularSearches.Count);
    }
}
