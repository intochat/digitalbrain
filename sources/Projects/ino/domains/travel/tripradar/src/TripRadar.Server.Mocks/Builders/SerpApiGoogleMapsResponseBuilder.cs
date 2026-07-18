using System.Text.Json;
using TripRadar.Server.Mocks.Responses.SerpApi.Maps;

namespace TripRadar.Server.Mocks.Builders;

/// <summary>
///     Builder for creating mock Google Maps API responses via SerpApi for testing purposes.
///     Provides realistic test data that matches the SerpApi Maps API response structure.
/// </summary>
public class SerpApiGoogleMapsResponseBuilder
{
    private string _address = "150 E 14th St, New York, NY 10003, United States";
    private string _placeId = "ChIJifIePKtZwokRVZ-UdRGkZzs";
    private string _price = "$$";
    private double? _rating = 4.2;
    private int? _reviews = 1250;
    private string _title = "Joe's Pizza";

    /// <summary>
    ///     Sets the place ID for the mock response
    /// </summary>
    public SerpApiGoogleMapsResponseBuilder WithPlaceId(string placeId)
    {
        _placeId = placeId;
        return this;
    }

    /// <summary>
    ///     Sets the place title for the mock response
    /// </summary>
    public SerpApiGoogleMapsResponseBuilder WithTitle(string title)
    {
        _title = title;
        return this;
    }

    /// <summary>
    ///     Sets the address for the mock response
    /// </summary>
    public SerpApiGoogleMapsResponseBuilder WithAddress(string address)
    {
        _address = address;
        return this;
    }

    /// <summary>
    ///     Sets the rating for the mock response
    /// </summary>
    public SerpApiGoogleMapsResponseBuilder WithRating(double rating)
    {
        _rating = rating;
        return this;
    }

    /// <summary>
    ///     Sets the number of reviews for the mock response
    /// </summary>
    public SerpApiGoogleMapsResponseBuilder WithReviews(int reviews)
    {
        _reviews = reviews;
        return this;
    }

    /// <summary>
    ///     Sets the price level for the mock response
    /// </summary>
    public SerpApiGoogleMapsResponseBuilder WithPrice(string price)
    {
        _price = price;
        return this;
    }

    /// <summary>
    ///     Builds the mock Google Maps response as JSON string
    /// </summary>
    public string Build()
    {
        var response = new GoogleMapsResponse
        {
            SearchMetadata =
                new SearchMetadata
                {
                    Id = "67890xyz",
                    Status = "Success",
                    JsonEndpoint = "https://serpapi.com/searches/67890xyz.json",
                    CreatedAt = "2024-01-15T10:30:00.000Z",
                    ProcessedAt = "2024-01-15T10:30:02.123Z",
                    RawHtmlFile = "https://serpapi.com/searches/67890xyz.html",
                    TotalTimeTaken = 2.123
                },
            SearchParameters =
                new MapsSearchParameters
                {
                    Engine = "google_maps",
                    Type = "place",
                    PlaceId = _placeId,
                    GoogleDomain = "google.com",
                    Hl = "en",
                    Gl = "us"
                },
            PlaceResults = new MapsPlaceResult
            {
                Title = _title,
                PlaceId = _placeId,
                DataId = "0x89c259a9b3117469:0x2edaf54e6b1e5b32",
                DataCid = "3373108189844415282",
                ReviewsLink = $"https://serpapi.com/search.json?engine=google_maps_reviews&place_id={_placeId}",
                PhotosLink = $"https://serpapi.com/search.json?engine=google_maps_photos&place_id={_placeId}",
                GpsCoordinates = new GpsCoordinates { Latitude = 40.7326, Longitude = -73.9885 },
                PlaceIdSearch = $"https://serpapi.com/search.json?engine=google_maps&place_id={_placeId}",
                ProviderId = "/g/1tg0s8km",
                Thumbnail =
                    "https://lh5.googleusercontent.com/p/AF1QipM8yKzW5P5Y7S8yKnBg8XYrK5wJQd6M2Z1X8YzC=w408-h306-k-no",
                SerpapiThumbnail = "https://serpapi.com/searches/67890xyz/images/abc123.jpeg",
                Rating = _rating,
                Reviews = _reviews,
                Price = _price,
                Type = new List<string> { "Pizza restaurant", "Italian restaurant", "Restaurant" },
                TypeIds = new List<string> { "pizza_restaurant", "italian_restaurant", "restaurant" },
                Description =
                    "Casual spot for NY-style pizza slices and Italian classics with outdoor seating.",
                Menu = new MapsMenu { Link = "http://joespizzanyc.com/menu", Source = "joespizzanyc.com" },
                OrderOnlineLink = "https://order.joespizzanyc.com",
                ServiceOptions =
                    new ServiceOptions { DineIn = true, Takeout = true, Delivery = true, Reservations = false },
                Extensions =
                    new List<MapsExtension>
                    {
                        new()
                        {
                            Highlights =
                                new List<string> { "Great pizza", "Casual dining", "Quick service" },
                            PopularFor = new List<string> { "Lunch", "Dinner", "Solo dining" },
                            Accessibility = new List<string> { "Wheelchair accessible entrance" },
                            Crowd = new List<string> { "Groups", "Tourists" },
                            Payments = new List<string> { "Credit cards", "Cash only" },
                            Planning = new List<string> { "Usually a wait" }
                        }
                    },
                Address = _address,
                Website = "http://joespizzanyc.com",
                Phone = "+1 212-555-0123",
                OpenState = "Open ⋅ Closes 11PM",
                PlusCode = "P2J6+2H New York",
                Hours =
                    new List<Dictionary<string, string>>
                    {
                        new() { ["day"] = "Monday", ["hours"] = "11AM–11PM" },
                        new() { ["day"] = "Tuesday", ["hours"] = "11AM–11PM" },
                        new() { ["day"] = "Wednesday", ["hours"] = "11AM–11PM" },
                        new() { ["day"] = "Thursday", ["hours"] = "11AM–11PM" },
                        new() { ["day"] = "Friday", ["hours"] = "11AM–12AM" },
                        new() { ["day"] = "Saturday", ["hours"] = "11AM–12AM" },
                        new() { ["day"] = "Sunday", ["hours"] = "11AM–11PM" }
                    },
                OperatingHours =
                    new Dictionary<string, string>
                    {
                        ["Monday"] = "11AM–11PM",
                        ["Tuesday"] = "11AM–11PM",
                        ["Wednesday"] = "11AM–11PM",
                        ["Thursday"] = "11AM–11PM",
                        ["Friday"] = "11AM–12AM",
                        ["Saturday"] = "11AM–12AM",
                        ["Sunday"] = "11AM–11PM"
                    },
                Images =
                    new List<MapsImage>
                    {
                        new()
                        {
                            Title = "All",
                            Thumbnail =
                                "https://lh5.googleusercontent.com/p/AF1QipM8yKzW5P5Y7S8yKnBg8XYrK5wJQd6M2Z1X8YzC=w408-h306-k-no"
                        },
                        new()
                        {
                            Title = "Food",
                            Thumbnail =
                                "https://lh5.googleusercontent.com/p/AF1QipNXDzY8K9K8Q1X8YzC8yKzW5P5Y7S8yKnBg8X=w408-h306-k-no"
                        }
                    },
                UserReviews =
                    new MapsUserReviews
                    {
                        Summary =
                            new List<MapsReviewSummary>
                            {
                                new()
                                {
                                    Snippet =
                                        "Great NY-style pizza with thin crust and fresh toppings"
                                },
                                new() { Snippet = "Fast service and authentic Italian flavors" }
                            },
                        MostRelevant =
                            new List<MapsReview>
                            {
                                new()
                                {
                                    Username = "John D.",
                                    Rating = 5,
                                    ContributorId = "117423892543167890123",
                                    Description =
                                        "Best pizza in the neighborhood! The margherita is fantastic and the staff is super friendly. Highly recommend!",
                                    Date = "2 weeks ago"
                                },
                                new()
                                {
                                    Username = "Sarah M.",
                                    Rating = 4,
                                    ContributorId = "105678901234567890456",
                                    Description =
                                        "Good pizza, reasonable prices. Can get crowded during lunch hours but worth the wait.",
                                    Date = "1 month ago"
                                }
                            }
                    },
                PopularTimes =
                    new MapsPopularTimes
                    {
                        LiveHash = new MapsLiveHash
                        {
                            Info = "Usually as busy as it gets",
                            TimeSpent = "People typically spend 30-45 min here"
                        }
                    },
                BookingLink = "https://resy.com/cities/ny/joespizza",
                Events = new List<MapsEvent>(),
                QuestionsAndAnswers = new List<MapsQA>
                {
                    new()
                    {
                        Question = new MapsQuestion
                        {
                            User = new MapsUser { Name = "Mike T.", LocalGuideLevel = 5 },
                            Text = "Do they have vegan cheese options?",
                            Date = "3 weeks ago"
                        },
                        Answer = new MapsAnswer
                        {
                            User = new MapsUser { Name = "Joe's Pizza", LocalGuideLevel = null },
                            Text = "Yes, we offer vegan mozzarella cheese for all our pizzas!",
                            Date = "3 weeks ago"
                        },
                        TotalAnswers = 1
                    }
                }
            }
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true
        };

        return JsonSerializer.Serialize(response, options);
    }

    /// <summary>
    ///     Creates a basic Maps response with default values
    /// </summary>
    public static string CreateBasicResponse()
    {
        return new SerpApiGoogleMapsResponseBuilder().Build();
    }

    /// <summary>
    ///     Creates a Maps response for a restaurant
    /// </summary>
    public static string CreateRestaurantResponse(string placeId, string name, string address)
    {
        return new SerpApiGoogleMapsResponseBuilder()
            .WithPlaceId(placeId)
            .WithTitle(name)
            .WithAddress(address)
            .WithRating(4.3)
            .WithReviews(892)
            .WithPrice("$$")
            .Build();
    }

    /// <summary>
    ///     Creates a Maps response for a hotel
    /// </summary>
    public static string CreateHotelResponse(string placeId, string name, string address)
    {
        return new SerpApiGoogleMapsResponseBuilder()
            .WithPlaceId(placeId)
            .WithTitle(name)
            .WithAddress(address)
            .WithRating(4.1)
            .WithReviews(543)
            .WithPrice("$$$")
            .Build();
    }

    /// <summary>
    ///     Creates a Maps response for an attraction
    /// </summary>
    public static string CreateAttractionResponse(string placeId, string name, string address)
    {
        return new SerpApiGoogleMapsResponseBuilder()
            .WithPlaceId(placeId)
            .WithTitle(name)
            .WithAddress(address)
            .WithRating(4.6)
            .WithReviews(2150)
            .Build();
    }
}
