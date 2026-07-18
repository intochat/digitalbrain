namespace TripRadar.MiniApp.Client.Infrastructure.Models.Hotels
{
    public sealed record HotelProperty(
        string? Type,
        string? Name,
        string? Description,
        string? Link,
        string? PropertyToken,
        GpsCoordinates? GpsCoordinates,
        string? CheckInTime,
        string? CheckOutTime,
        HotelRate? RatePerNight,
        HotelRate? TotalRate,
        string? Deal,
        string? DealDescription,
        string? HotelClass,
        int? ExtractedHotelClass,
        List<HotelImage>? Images,
        double? OverallRating,
        int? Reviews,
        double? LocationRating,
        List<string>? Amenities,
        List<NearbyPlace>? NearbyPlaces,
        List<HotelPrice>? Prices,
        bool? EcoCertified
    );
}