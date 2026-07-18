namespace TripRadar.Server.Application.Constants.Hotels;

/// <summary>
///     Constants for Google Hotels API query parameters and values
/// </summary>
public static class HotelQueryConstants
{
    /// <summary>
    ///     Sort options for hotel search results
    ///     3: Recommended (default)
    ///     8: Price (low to high)
    ///     13: Guest rating
    /// </summary>
    public static readonly string[] SortBy = ["3", "8", "13"];

    /// <summary>
    ///     Property types for hotels
    ///     12: Hotel
    ///     13: Resort
    ///     14: Motel
    ///     15: Bed and breakfast
    ///     16: Guest house
    ///     17: Hostel
    ///     18: Inn
    ///     19: Lodge
    ///     20: Ryokan
    ///     21: All-inclusive
    ///     22: Boutique hotel
    ///     23: Business hotel
    ///     24: Casino hotel
    /// </summary>
    public static readonly string[] HotelPropertyTypes =
        ["12", "13", "14", "15", "16", "17", "18", "19", "20", "21", "22", "23", "24"];

    /// <summary>
    ///     Property types for vacation rentals
    ///     1: Apartment
    ///     2: House
    ///     3: Villa
    ///     4: Condo
    ///     5: Cabin
    ///     6: Chalet
    ///     7: Cottage
    ///     8: Bungalow
    ///     9: Farm stay
    ///     10: Guest suite
    ///     11: Guest house
    ///     21: All-inclusive
    /// </summary>
    public static readonly string[] VacationRentalPropertyTypes =
        ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "21"];

    /// <summary>
    ///     Common amenities available in hotels
    ///     1: Free Wi-Fi
    ///     3: Pool
    ///     4: Fitness center
    ///     5: Restaurant
    ///     6: Bar/Lounge
    ///     7: Room service
    ///     8: Business center
    ///     9: Spa
    ///     10: Parking
    ///     11: Breakfast included
    ///     12: Air conditioning
    ///     15: Pet friendly
    ///     19: Beach access
    ///     22: Airport shuttle
    ///     35: Free cancellation
    ///     40: Non-smoking rooms
    ///     52: Family rooms
    ///     53: Accessible rooms
    ///     61: 24-hour front desk
    /// </summary>
    public static readonly string[] CommonAmenities =
        ["1", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "15", "19", "22", "35", "40", "52", "53", "61"];

    /// <summary>
    ///     Amenities specific to vacation rentals
    ///     2: Kitchen
    ///     4: Washer
    ///     6: Dryer
    ///     10: Balcony/Patio
    ///     12: Air conditioning
    ///     15: Pet friendly
    ///     16: Fireplace
    ///     18: Garden
    ///     20: Hot tub
    ///     21: Private pool
    ///     24: BBQ facilities
    ///     29: Outdoor dining area
    ///     32: Free parking
    /// </summary>
    public static readonly string[] VacationRentalAmenities =
        ["2", "4", "6", "10", "12", "15", "16", "18", "20", "21", "24", "29", "32"];

    /// <summary>
    ///     Room types based on Google Hotels API
    ///     1: Standard
    ///     2: Deluxe
    ///     3: Suite
    ///     4: Executive
    ///     5: Family
    ///     6: Studio
    ///     7: Villa
    ///     8: Apartment
    ///     9: Bungalow
    ///     10: Cottage
    /// </summary>
    public static readonly string[] RoomTypes = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10"];

    /// <summary>
    ///     Hotel classes (star ratings) based on Google Hotels API
    ///     1: 1 star
    ///     2: 2 stars
    ///     3: 3 stars
    ///     4: 4 stars
    ///     5: 5 stars
    /// </summary>
    public static readonly string[] HotelClasses = ["1", "2", "3", "4", "5"];
}
