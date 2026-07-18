using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.Mocks.Builders;

public class SerpApiGoogleHotelResponseBuilder
{
    public GetHotelResponseDTO GetHotelData(string location, string checkIn, string checkOut, int adults = 2,
        int children = 0, string currency = "USD", string languageCode = "en", string countryCode = "us",
        int? minPrice = null, int? maxPrice = null)
    {
        return new GetHotelResponseDTO
        {
            SearchMetadata =
                new SearchMetadata
                {
                    Id = Guid.NewGuid().ToString(),
                    Status = "Success",
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                },
            SearchParameters =
                new HotelSearchParameters
                {
                    Engine = "google_hotels",
                    Query = location,
                    CheckInDate = checkIn,
                    CheckOutDate = checkOut,
                    Adults = adults,
                    Children = children,
                    Currency = currency,
                    LanguageCode = languageCode,
                    CountryCode = countryCode
                },
            SearchInformation = new SearchInformation { TotalResults = 1234 },
            Properties = GenerateProperties(minPrice, maxPrice),
            Brands = GenerateBrands(),
            SerpapiPagination = new SerpapiPagination
            {
                Next = "eyJvZmZzZXQiOjIwLCJuIjoiMjAifQ==", NextPageToken = "next_page_token_123"
            }
        };
    }

    private List<Property> GenerateProperties(int? minPrice = null, int? maxPrice = null)
    {
        var properties = new List<Property>
        {
            new()
            {
                Type = "hotel",
                Name = "Luxury Grand Hotel",
                Description =
                    "Experience luxury like never before at our 5-star hotel in the heart of the city.",
                Link = "https://example.com/luxury-grand-hotel",
                PropertyToken = "luxury-grand-hotel-token-123",
                SerpapiPropertyDetailsLink = "https://serpapi.com/property/details/luxury-grand-hotel",
                GpsCoordinates = new GpsCoordinates { Latitude = 40.7129, Longitude = -74.0061 },
                CheckInTime = "3:00 PM",
                CheckOutTime = "11:00 AM",
                RatePerNight =
                    new Rate
                    {
                        Lowest = $"${FilterPriceByRange(299.99m, minPrice, maxPrice)}",
                        ExtractedLowest = FilterPriceByRange(299.99m, minPrice, maxPrice),
                        BeforeTaxesFees = $"${FilterPriceByRange(299.99m, minPrice, maxPrice)}",
                        ExtractedBeforeTaxesFees = FilterPriceByRange(299.99m, minPrice, maxPrice)
                    },
                TotalRate =
                    new Rate
                    {
                        Lowest = $"${FilterPriceByRange(299.99m, minPrice, maxPrice) * 2}",
                        ExtractedLowest = FilterPriceByRange(299.99m, minPrice, maxPrice) * 2,
                        BeforeTaxesFees = $"${FilterPriceByRange(299.99m, minPrice, maxPrice) * 2}",
                        ExtractedBeforeTaxesFees = FilterPriceByRange(299.99m, minPrice, maxPrice) * 2
                    },
                Amenities = ["Spa", "Pool", "Gym", "Restaurant", "Bar", "Room Service", "Free WiFi"],
                EcoCertified = true,
                Images =
                    new List<Image>
                    {
                        new()
                        {
                            Thumbnail = "https://example.com/images/luxury-grand-hotel-thumb.jpg",
                            OriginalImage = "https://example.com/images/luxury-grand-hotel-1.jpg"
                        },
                        new()
                        {
                            Thumbnail = "https://example.com/images/luxury-grand-hotel-thumb2.jpg",
                            OriginalImage = "https://example.com/images/luxury-grand-hotel-2.jpg"
                        }
                    },
                Ratings =
                    new List<Rating>
                    {
                        new() { Stars = 5, Count = 800 },
                        new() { Stars = 4, Count = 120 },
                        new() { Stars = 3, Count = 45 },
                        new() { Stars = 2, Count = 20 },
                        new() { Stars = 1, Count = 13 }
                    },
                ReviewsBreakdown =
                    new List<ReviewBreakdown>
                    {
                        new() { Name = "Service", Positive = 4 },
                        new() { Name = "Cleanliness", Positive = 4 },
                        new() { Name = "Value", Positive = 4 },
                        new() { Name = "Location", Positive = 4 }
                    },
                NearbyPlaces =
                    new List<NearbyPlace>
                    {
                        new()
                        {
                            Name = "Central Park",
                            Transportations =
                                new List<Transportation>
                                {
                                    new() { Type = "Walking", Duration = "5 min" }
                                }
                        },
                        new()
                        {
                            Name = "Times Square",
                            Transportations =
                                new List<Transportation>
                                {
                                    new() { Type = "Subway", Duration = "10 min" }
                                }
                        }
                    }
            },
            new()
            {
                Type = "hotel",
                Name = "Comfort Inn Express",
                Description =
                    "Comfortable and affordable accommodation perfect for business and leisure travelers.",
                Link = "https://example.com/comfort-inn-express",
                PropertyToken = "comfort-inn-express-token-456",
                SerpapiPropertyDetailsLink = "https://serpapi.com/property/details/comfort-inn-express",
                GpsCoordinates = new GpsCoordinates { Latitude = 40.7150, Longitude = -74.0070 },
                CheckInTime = "2:00 PM",
                CheckOutTime = "12:00 PM",
                RatePerNight =
                    new Rate
                    {
                        Lowest = $"${FilterPriceByRange(149.99m, minPrice, maxPrice)}",
                        ExtractedLowest = FilterPriceByRange(149.99m, minPrice, maxPrice),
                        BeforeTaxesFees = $"${FilterPriceByRange(149.99m, minPrice, maxPrice)}",
                        ExtractedBeforeTaxesFees = FilterPriceByRange(149.99m, minPrice, maxPrice)
                    },
                TotalRate =
                    new Rate
                    {
                        Lowest = $"${FilterPriceByRange(149.99m, minPrice, maxPrice) * 2}",
                        ExtractedLowest = FilterPriceByRange(149.99m, minPrice, maxPrice) * 2,
                        BeforeTaxesFees = $"${FilterPriceByRange(149.99m, minPrice, maxPrice) * 2}",
                        ExtractedBeforeTaxesFees = FilterPriceByRange(149.99m, minPrice, maxPrice) * 2
                    },
                Amenities = ["Continental Breakfast", "Gym", "Business Center", "Free WiFi", "Free Parking"],
                EcoCertified = false,
                Images =
                    new List<Image>
                    {
                        new()
                        {
                            Thumbnail = "https://example.com/images/comfort-inn-thumb.jpg",
                            OriginalImage = "https://example.com/images/comfort-inn-1.jpg"
                        }
                    },
                Ratings =
                    new List<Rating>
                    {
                        new() { Stars = 5, Count = 400 },
                        new() { Stars = 4, Count = 200 },
                        new() { Stars = 3, Count = 80 },
                        new() { Stars = 2, Count = 30 },
                        new() { Stars = 1, Count = 15 }
                    },
                ReviewsBreakdown = new List<ReviewBreakdown>
                {
                    new() { Name = "Service", Positive = 4 },
                    new() { Name = "Cleanliness", Positive = 4 },
                    new() { Name = "Value", Positive = 4 },
                    new() { Name = "Location", Positive = 4 }
                },
                NearbyPlaces = new List<NearbyPlace>
                {
                    new()
                    {
                        Name = "Shopping Mall",
                        Transportations = new List<Transportation>
                        {
                            new() { Type = "Walking", Duration = "2 min" }
                        }
                    }
                }
            }
        };

        return properties;
    }

    private List<Brand> GenerateBrands()
    {
        return new List<Brand>
        {
            new()
            {
                Id = 10,
                Name = "Marriott",
                Children =
                    new List<BrandChild>
                    {
                        new() { Id = 11, Name = "Marriott Hotels" }, new() { Id = 12, Name = "Marriott Courtyard" }
                    }
            },
            new()
            {
                Id = 20,
                Name = "Hilton",
                Children = new List<BrandChild>
                {
                    new() { Id = 21, Name = "Hilton Hotels" }, new() { Id = 22, Name = "Hampton Inn" }
                }
            }
        };
    }

    private decimal FilterPriceByRange(decimal originalPrice, int? minPrice, int? maxPrice)
    {
        if (minPrice.HasValue && originalPrice < minPrice.Value)
        {
            return minPrice.Value;
        }

        if (maxPrice.HasValue && originalPrice > maxPrice.Value)
        {
            return maxPrice.Value;
        }

        return originalPrice;
    }
}
