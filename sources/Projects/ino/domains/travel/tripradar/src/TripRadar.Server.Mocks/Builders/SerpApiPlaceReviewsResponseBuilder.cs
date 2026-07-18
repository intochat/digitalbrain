using System.Text.Json;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Mocks.Responses.SerpApi.MapsReviews;

namespace TripRadar.Server.Mocks.Builders;

public class SerpApiPlaceReviewsResponseBuilder
{
    private readonly ILogger<SerpApiPlaceReviewsResponseBuilder> _logger;
    private readonly Dictionary<string, GoogleMapsReviewsResponse> _mockReviewsData = new();

    public SerpApiPlaceReviewsResponseBuilder(ILogger<SerpApiPlaceReviewsResponseBuilder> logger)
    {
        _logger = logger;
        InitializeMockData();
    }

    public GoogleMapsReviewsResponse GetPlaceReviews(string? placeId, string? dataId, string? sortBy = null,
        string? topicId = null, int num = 10)
    {
        var identifier = placeId ?? dataId ?? "default_place";
        var cacheKey = $"place={identifier}&sort={sortBy}&topic={topicId}";

        GoogleMapsReviewsResponse baseData;
        if (_mockReviewsData.TryGetValue(cacheKey, out var mockData))
        {
            baseData = mockData;
        }
        else
        {
            var placeKey = $"place={identifier}";
            if (_mockReviewsData.TryGetValue(placeKey, out var placeData))
            {
                baseData = ModifySortedResults(placeData, sortBy, topicId);
                _mockReviewsData[cacheKey] = baseData;
            }
            else
            {
                _logger.LogInformation("Creating new mock review data for place {PlaceId}", identifier);
                baseData = CreateMockPlaceReviewsData(identifier, sortBy);
                _mockReviewsData[cacheKey] = baseData;
                _mockReviewsData[placeKey] = baseData;
            }
        }

        var paginatedData = ApplyPagination(baseData, num);
        _logger.LogInformation("Returning paginated mock review data for place {PlaceId} (num={Num})", identifier, num);
        return paginatedData;
    }

    private GoogleMapsReviewsResponse ApplyPagination(GoogleMapsReviewsResponse source, int num)
    {
        if (source.Reviews == null || !source.Reviews.Any())
        {
            return source;
        }

        var clone = JsonSerializer.Deserialize<GoogleMapsReviewsResponse>(JsonSerializer.Serialize(source));
        if (clone?.Reviews == null)
        {
            return source;
        }

        var paginatedResults = clone.Reviews.Take(num).ToList();

        clone.Reviews = paginatedResults;

        if (source.Reviews.Count > num)
        {
            var nextPageToken = SafeSubstring(Guid.NewGuid().ToString("N"), 40).ToUpper();
            clone.Pagination = new Pagination
            {
                NextPageToken = nextPageToken,
                SerpApiPagination = new SerpApiPagination
                {
                    Next =
                        $"https://serpapi.com/search.json?engine=google_maps_reviews&hl=en&next_page_token={nextPageToken}&place_id={source.SearchParameters?.PlaceId}",
                    NextPageToken = nextPageToken
                }
            };
        }

        return clone;
    }

    private static string SafeSubstring(string input, int length)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return input.Length <= length ? input : input.Substring(0, length);
    }

    private GoogleMapsReviewsResponse ModifySortedResults(GoogleMapsReviewsResponse source, string? sortBy,
        string? topicId)
    {
        var clone = JsonSerializer.Deserialize<GoogleMapsReviewsResponse>(JsonSerializer.Serialize(source));
        if (clone == null)
        {
            return source;
        }

        if (clone.SearchParameters != null)
        {
            clone.SearchParameters.SortBy = sortBy;
            clone.SearchParameters.TopicId = topicId;
        }

        if (clone.Reviews == null || !clone.Reviews.Any())
        {
            return clone;
        }

        clone.Reviews = sortBy switch
        {
            "ratingHigh" => clone.Reviews.OrderByDescending(r => r.Rating).ToList(),
            "ratingLow" => clone.Reviews.OrderBy(r => r.Rating).ToList(),
            "newestFirst" => clone.Reviews.OrderByDescending(r => ParseDate(r.Date)).ToList(),
            "qualityScore" => clone.Reviews.OrderByDescending(r => (r.Likes ?? 0) + r.Rating).ToList(),
            _ => clone.Reviews.ToList()
        };

        if (!string.IsNullOrEmpty(topicId))
        {
            foreach (var review in clone.Reviews)
            {
                if (review.Snippet != null)
                {
                    review.Snippet = topicId switch
                    {
                        "food" => review.Snippet.Replace("experience", "food").Replace("service", "meal"),
                        "service" => review.Snippet.Replace("experience", "service").Replace("food", "staff"),
                        "atmosphere" => review.Snippet.Replace("experience", "atmosphere").Replace("food", "ambiance"),
                        _ => review.Snippet
                    };
                }
            }
        }

        return clone;
    }

    private DateTime ParseDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr))
        {
            return DateTime.MinValue;
        }

        if (dateStr.Contains("day"))
        {
            var days = int.TryParse(dateStr.Split(' ')[0], out var d) ? d : 1;
            return DateTime.UtcNow.AddDays(-days);
        }

        if (dateStr.Contains("week"))
        {
            var weeks = int.TryParse(dateStr.Split(' ')[0], out var w) ? w : 1;
            return DateTime.UtcNow.AddDays(-weeks * 7);
        }

        if (dateStr.Contains("month"))
        {
            var months = int.TryParse(dateStr.Split(' ')[0], out var m) ? m : 1;
            return DateTime.UtcNow.AddMonths(-months);
        }

        return DateTime.UtcNow.AddDays(-30);
    }

    private GoogleMapsReviewsResponse CreateMockPlaceReviewsData(string identifier, string? sortBy)
    {
        var searchId = SafeSubstring(Guid.NewGuid().ToString("N"), 24);
        var searchMetadata = new SearchMetadata
        {
            Id = searchId,
            Status = "Success",
            JsonEndpoint = $"https://serpapi.com/searches/{SafeSubstring(searchId, 16)}/{searchId}.json",
            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"),
            ProcessedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"),
            GoogleMapsReviewsUrl =
                $"https://www.google.com/maps/place/data=!4m7!3m6!1s{identifier}!5m2!4m1!1i2!9m1!1b1?hl=en&gl=",
            GoogleUrl = $"https://www.google.com/maps/place/{identifier}/reviews",
            RawHtmlFile = $"https://serpapi.com/searches/{SafeSubstring(searchId, 16)}/{searchId}.html",
            PrettifyHtmlFile = $"https://serpapi.com/searches/{SafeSubstring(searchId, 16)}/{searchId}.prettify",
            TotalTimeTaken = Math.Round(1.0 + new Random().NextDouble() * 2.0, 2)
        };

        var searchParameters = new SearchParameters
        {
            Engine = "google_maps_reviews",
            PlaceId = identifier.StartsWith("ChIJ") ? identifier : null,
            DataId = !identifier.StartsWith("ChIJ") ? identifier : null,
            SortBy = sortBy,
            Hl = "en"
        };

        var placeInfo = GeneratePlaceInfo(identifier);
        var topics = GenerateTopics();
        var reviews = GenerateReviews(identifier);

        return new GoogleMapsReviewsResponse
        {
            SearchMetadata = searchMetadata,
            SearchParameters = searchParameters,
            PlaceInfo = placeInfo,
            Topics = topics,
            Reviews = reviews
        };
    }

    private PlaceInfo GeneratePlaceInfo(string identifier)
    {
        var random = new Random();
        var businessNames = new[]
        {
            "The Grand Cafe", "Sunset Restaurant", "Urban Bistro", "Coastal Dining", "Garden View"
        };
        var businessTypes = new[] { "Restaurant", "Cafe", "Hotel", "Store", "Service" };

        var hasNoReviews = identifier is "place_789" or "place_invalid";

        return new PlaceInfo
        {
            Title = businessNames[random.Next(businessNames.Length)],
            DataId =
                identifier.StartsWith("ChIJ")
                    ? $"0x{random.Next(1000000, 9999999):X}:{random.Next(1000000, 9999999):X}"
                    : identifier,
            PlaceId =
                identifier.StartsWith("ChIJ")
                    ? identifier
                    : $"ChIJ{SafeSubstring(Guid.NewGuid().ToString("N"), 16)}",
            GpsCoordinates =
                new GpsCoordinates
                {
                    Latitude = 40.7128 + (random.NextDouble() - 0.5) * 0.1,
                    Longitude = -74.0060 + (random.NextDouble() - 0.5) * 0.1
                },
            Rating = hasNoReviews ? 0 : Math.Round(3.5 + random.NextDouble() * 1.5, 1),
            Reviews = hasNoReviews ? 0 : random.Next(100, 2000),
            Type = businessTypes[random.Next(businessTypes.Length)],
            Address = "123 Main St, New York, NY 10001",
            Phone = "+1 (*************",
            Website = "https://example.com",
            Description = "A wonderful place with great service and atmosphere."
        };
    }

    private List<Topic> GenerateTopics()
    {
        return
        [
            new() { Keyword = "thai food", Mentions = 84, Id = "/m/07hxn" },
            new() { Keyword = "green curry", Mentions = 10, Id = "/m/091qsl" },
            new() { Keyword = "laksa", Mentions = 9, Id = "/m/027rch" },
            new() { Keyword = "red curry", Mentions = 7, Id = "/g/121kvy6z" },
            new() { Keyword = "pad see ew", Mentions = 7, Id = "/m/0dl1v4" },
            new() { Keyword = "spicy fried rice", Mentions = 6, Id = "/g/11j3v0jq1y" },
            new() { Keyword = "CBD", Mentions = 6, Id = "/m/04klcx" },
            new() { Keyword = "satay", Mentions = 5, Id = "/m/01crps" },
            new() { Keyword = "great value", Mentions = 5, Id = "/g/11c2q5_vnf" },
            new() { Keyword = "soy chicken", Mentions = 5, Id = "/m/050jxs" }
        ];
    }

    private List<Review> GenerateReviews(string? identifier = null)
    {
        if (identifier == "place_789" || identifier == "place_invalid")
        {
            return [];
        }

        var random = new Random();
        var reviewerNames = new[]
        {
            "John Smith", "Sarah Johnson", "Mike Brown", "Emily Davis", "David Wilson", "Lisa Anderson",
            "Chris Taylor", "Amanda Garcia", "Ryan Martinez", "Jessica Lee"
        };

        var reviewSnippets = new[]
        {
            "Great experience! The service was excellent and the atmosphere was perfect.",
            "Good food but the service could be improved. Overall a decent place.",
            "Amazing! Everything was perfect from start to finish. Highly recommended.",
            "Average experience. Nothing special but nothing terrible either.",
            "Fantastic place! Will definitely come back again. The staff was very friendly.",
            "Disappointing visit. Food was cold and service was slow.",
            "Beautiful atmosphere and delicious food. A bit pricey but worth it.",
            "Quick service and good quality. Perfect for a lunch break.",
            "Outstanding! One of the best places I've been to in the area.",
            "Nice place with good food. The location is convenient and parking is easy."
        };

        return Enumerable.Range(1, random.Next(15, 50))
            .Select(i =>
            {
                var reviewId = $"ChZDSUhNMG9nS0VJQ0Fn{SafeSubstring(Guid.NewGuid().ToString("N"), 16)}EAE";
                var contributorId = random.Next(100000000, 999999999) + random.Next(100000000, 999999999);
                var dateStr = GenerateRandomDate(random);
                var isoDate = GenerateIsoDate(dateStr);
                var hasOwnerResponse = random.NextDouble() > 0.7;
                var ownerResponseDate = hasOwnerResponse ? GenerateRandomDate(random) : null;

                return new Review
                {
                    Link =
                        $"https://www.google.com/maps/reviews/data=!4m8!14m7!1m6!2m5!1s{reviewId}!2m1!1s0x0:0x4dee024d9552f4d1!3m1!1s2@1:{reviewId}%7CCgwI{SafeSubstring(Guid.NewGuid().ToString("N"), 12)}%7C?hl=en-US",
                    Position = i,
                    User =
                        new User
                        {
                            Name = reviewerNames[random.Next(reviewerNames.Length)],
                            Link = $"https://www.google.com/maps/contrib/{contributorId}?hl=en-US",
                            ContributorId = contributorId.ToString(),
                            Thumbnail =
                                $"https://lh3.googleusercontent.com/a-/ALV-UjXdrhHpdlXx8I-hEsW-h9Oxl0a0ThkN7M6AFbU3w06Y6wdB1sg=s120-c-rp-mo-ba{random.Next(1, 8)}-br100",
                            LocalGuide = random.NextDouble() > 0.6,
                            Reviews = random.Next(5, 500),
                            Photos = random.Next(0, 100)
                        },
                    Rating = Math.Round(1.0 + random.NextDouble() * 4.0, 1),
                    Date = dateStr,
                    IsoDate = isoDate,
                    IsoDateOfLastEdit = isoDate,
                    Snippet = reviewSnippets[random.Next(reviewSnippets.Length)],
                    ExtractedSnippet =
                        new ExtractedSnippet { Original = reviewSnippets[random.Next(reviewSnippets.Length)] },
                    Likes = random.NextDouble() > 0.5 ? random.Next(0, 20) : null,
                    Images = GenerateReviewImageUrls(random),
                    Source = "Google",
                    ReviewId = reviewId,
                    LocalGuide = random.NextDouble() > 0.6,
                    Details = GenerateReviewDetails(random),
                    Response = hasOwnerResponse
                        ? new OwnerResponse
                        {
                            Date = ownerResponseDate,
                            IsoDate = GenerateIsoDate(ownerResponseDate!),
                            IsoDateOfLastEdit = GenerateIsoDate(ownerResponseDate!),
                            Snippet = "Thank you for your feedback! We appreciate your visit.",
                            ExtractedSnippet =
                                new ExtractedSnippet
                                {
                                    Original = "Thank you for your feedback! We appreciate your visit."
                                }
                        }
                        : null,
                    ResponseFromOwnerText =
                        hasOwnerResponse ? "Thank you for your feedback! We appreciate your visit." : null,
                    ResponseFromOwnerAgo = hasOwnerResponse ? ownerResponseDate : null
                };
            })
            .ToList();
    }

    private string GenerateRandomDate(Random random)
    {
        var options = new[]
        {
            $"{random.Next(1, 30)} days ago", $"{random.Next(1, 4)} weeks ago", $"{random.Next(1, 12)} months ago",
            "yesterday", "2 days ago", "1 week ago"
        };

        return options[random.Next(options.Length)];
    }

    private string GenerateIsoDate(string dateStr)
    {
        var parsed = ParseDate(dateStr);
        return parsed == DateTime.MinValue
            ? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            : parsed.ToString("yyyy-MM-ddTHH:mm:ssZ");
    }

    private List<string>? GenerateReviewImageUrls(Random random)
    {
        // Only some reviews have images (about 30% chance)
        if (random.NextDouble() > 0.3)
        {
            return null;
        }

        var imageCount = random.Next(1, 4); // 1-3 images per review
        return Enumerable.Range(1, imageCount)
            .Select(_ => $"https://lh3.googleusercontent.com/geougc-cs/AB3l90B{SafeSubstring(Guid.NewGuid().ToString("N"), 40)}")
            .ToList();
    }

    private ReviewDetails GenerateReviewDetails(Random random)
    {
        var mealTypes = new[] { "Lunch", "Dinner", "Breakfast", "Brunch" };
        var priceRanges = new[] { "A$1–20", "A$20–40", "A$40–60", "A$60–80" };
        var dishes = new[] { "Vegan, Green Curry", "Pad Thai", "Soy Chicken", "Spicy Fried Rice", "Jungle Curry" };

        return new ReviewDetails
        {
            Service = random.Next(1, 6),
            MealType = mealTypes[random.Next(mealTypes.Length)],
            PricePerPerson = priceRanges[random.Next(priceRanges.Length)],
            Food = random.Next(1, 6),
            Atmosphere = random.Next(1, 6),
            RecommendedDishes = dishes[random.Next(dishes.Length)],
            VegetarianOptions = random.NextDouble() > 0.5 ? "Yes" : null,
            DietaryRestrictions = random.NextDouble() > 0.5 ? "Yes" : null,
            KidFriendliness = random.NextDouble() > 0.5 ? "Yes" : null,
            WheelchairAccessibility = random.NextDouble() > 0.5 ? "Yes" : null
        };
    }

    private void InitializeMockData()
    {
        var commonPlaces = new[]
        {
            ("ChIJN1t_tDeuEmsRUsoyG83frY4", "The Famous Restaurant"),
            ("ChIJN2t_tDeuEmsRUsoyG83frY5", "Downtown Cafe"), ("ChIJN3t_tDeuEmsRUsoyG83frY6", "City Hotel")
        };

        foreach (var (placeId, name) in commonPlaces)
        {
            var mockData = CreateMockPlaceReviewsData(placeId, null);
            if (mockData.PlaceInfo != null)
            {
                mockData.PlaceInfo.Title = name;
            }

            _mockReviewsData[$"place={placeId}"] = mockData;
        }

        _logger.LogInformation("Initialized mock place reviews data with {Count} places", commonPlaces.Length);
    }
}
