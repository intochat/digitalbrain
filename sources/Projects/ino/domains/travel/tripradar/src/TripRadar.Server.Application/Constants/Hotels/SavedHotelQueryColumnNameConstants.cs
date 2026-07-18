namespace TripRadar.Server.Application.Constants.Hotels;

public static class SavedHotelQueryColumnNameConstants
{
    // Root level response properties
    public const string SearchMetadata = "search_metadata";
    public const string SearchParameters = "search_parameters";
    public const string SearchInformation = "search_information";
    public const string Brands = "brands";
    public const string Properties = "properties";
    public const string SerpapiPagination = "serpapi_pagination";

    // Property properties
    public const string Type = "type";
    public const string Name = "name";
    public const string Description = "description";
    public const string Link = "link";
    public const string PropertyToken = "property_token";
    public const string SerpapiPropertyDetailsLink = "serpapi_property_details_link";
    public const string GpsCoordinates = "gps_coordinates";
    public const string CheckInTime = "check_in_time";
    public const string CheckOutTime = "check_out_time";
    public const string RatePerNight = "rate_per_night";
    public const string TotalRate = "total_rate";
    public const string Deal = "deal";
    public const string DealDescription = "deal_description";
    public const string NearbyPlaces = "nearby_places";
    public const string HotelClass = "hotel_class";
    public const string ExtractedHotelClass = "extracted_hotel_class";
    public const string Images = "images";
    public const string OverallRating = "overall_rating";
    public const string Reviews = "reviews";
    public const string Ratings = "ratings";
    public const string LocationRating = "location_rating";
    public const string ReviewsBreakdown = "reviews_breakdown";
    public const string Amenities = "amenities";
    public const string ExcludedAmenities = "excluded_amenities";
    public const string EssentialInfo = "essential_info";
    public const string EcoCertified = "eco_certified";
    public const string Prices = "prices";

    // GPS Coordinates properties
    public const string Latitude = "latitude";
    public const string Longitude = "longitude";

    // Rate properties
    public const string Lowest = "lowest";
    public const string ExtractedLowest = "extracted_lowest";
    public const string BeforeTaxesFees = "before_taxes_fees";
    public const string ExtractedBeforeTaxesFees = "extracted_before_taxes_fees";

    // Nearby Place properties
    public const string PlaceName = "name";
    public const string Transportations = "transportations";

    // Transportation properties
    public const string TransportationType = "type";
    public const string Duration = "duration";

    // Image properties
    public const string Thumbnail = "thumbnail";
    public const string OriginalImage = "original_image";

    // Rating properties
    public const string Stars = "stars";
    public const string Count = "count";

    // Review Breakdown properties
    public const string ReviewName = "name";
    public const string ReviewDescription = "description";
    public const string TotalMentioned = "total_mentioned";
    public const string Positive = "positive";
    public const string Negative = "negative";
    public const string Neutral = "neutral";

    // Price properties
    public const string Source = "source";
    public const string Logo = "logo";
    public const string NumGuests = "num_guests";
    public const string FreeCancellation = "free_cancellation";
    public const string FreeCancellationUntilDate = "free_cancellation_until_date";
    public const string FreeCancellationUntilTime = "free_cancellation_until_time";

    // Brand properties
    public const string BrandId = "id";
    public const string BrandName = "name";
    public const string BrandChildren = "children";

    // Search Information properties
    public const string TotalResults = "total_results";
}
