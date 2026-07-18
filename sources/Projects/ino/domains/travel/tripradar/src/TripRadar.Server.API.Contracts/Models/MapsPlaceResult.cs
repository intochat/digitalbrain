namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Comprehensive place information from Google Maps
/// </summary>
public class MapsPlaceResult
{
    /// <summary>
    ///     Name of the place
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    ///     Unique Google Place ID
    /// </summary>
    public string PlaceId { get; set; } = string.Empty;

    /// <summary>
    ///     Data ID for the place
    /// </summary>
    public string? DataId { get; set; }

    /// <summary>
    ///     CID (Customer ID) for the place
    /// </summary>
    public string? DataCid { get; set; }

    /// <summary>
    ///     Link to fetch place reviews via SerpApi
    /// </summary>
    public string? ReviewsLink { get; set; }

    /// <summary>
    ///     Link to fetch place photos via SerpApi
    /// </summary>
    public string? PhotosLink { get; set; }

    /// <summary>
    ///     GPS coordinates of the place
    /// </summary>
    public GpsCoordinates? GpsCoordinates { get; set; }

    /// <summary>
    ///     Link to search for this place via SerpApi
    /// </summary>
    public string? PlaceIdSearch { get; set; }

    /// <summary>
    ///     Provider ID for the place
    /// </summary>
    public string? ProviderId { get; set; }

    /// <summary>
    ///     Main thumbnail image URL
    /// </summary>
    public string? Thumbnail { get; set; }

    /// <summary>
    ///     SerpApi processed thumbnail URL
    /// </summary>
    public string? SerpapiThumbnail { get; set; }

    /// <summary>
    ///     Average rating (0-5 stars)
    /// </summary>
    public double? Rating { get; set; }

    /// <summary>
    ///     Total number of reviews
    /// </summary>
    public int? Reviews { get; set; }

    /// <summary>
    ///     Price level indicator ($, $$, $$$, $$$$)
    /// </summary>
    public string? Price { get; set; }

    /// <summary>
    ///     Place type (can be single type or comma-separated types like "Café, Restaurant")
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    ///     List of place type IDs (e.g., "restaurant", "italian_restaurant")
    /// </summary>
    public List<string>? TypeIds { get; set; }

    /// <summary>
    ///     Description of the place
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    ///     Menu information if available
    /// </summary>
    public MapsMenu? Menu { get; set; }

    /// <summary>
    ///     Link for online ordering
    /// </summary>
    public string? OrderOnlineLink { get; set; }

    /// <summary>
    ///     Available service options (dine-in, takeout, delivery, etc.)
    /// </summary>
    public ServiceOptions? ServiceOptions { get; set; }

    /// <summary>
    ///     Additional place information and features
    /// </summary>
    public List<MapsExtension>? Extensions { get; set; }

    /// <summary>
    ///     Extensions not yet supported by SerpApi
    /// </summary>
    public List<MapsExtension>? UnsupportedExtensions { get; set; }

    /// <summary>
    ///     Physical address of the place
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    ///     Official website URL
    /// </summary>
    public string? Website { get; set; }

    /// <summary>
    ///     Phone number
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    ///     Current open/closed status
    /// </summary>
    public string? OpenState { get; set; }

    /// <summary>
    ///     Plus code for the location
    /// </summary>
    public string? PlusCode { get; set; }

    /// <summary>
    ///     Operating hours as a human-readable string (e.g., "Ouvert ⋅ Ferme à 18:00")
    /// </summary>
    public string? Hours { get; set; }

    /// <summary>
    ///     Structured operating hours
    /// </summary>
    public Dictionary<string, string>? OperatingHours { get; set; }

    /// <summary>
    ///     Gallery images of the place
    /// </summary>
    public List<MapsImage>? Images { get; set; }

    /// <summary>
    ///     User reviews and ratings
    /// </summary>
    public MapsUserReviews? UserReviews { get; set; }

    /// <summary>
    ///     Related places people also search for
    /// </summary>
    public List<MapsRelatedSearch>? PeopleAlsoSearchFor { get; set; }

    /// <summary>
    ///     Popular times and busy hours
    /// </summary>
    public MapsPopularTimes? PopularTimes { get; set; }

    /// <summary>
    ///     Link for making reservations
    /// </summary>
    public string? BookingLink { get; set; }

    /// <summary>
    ///     Table reservation link
    /// </summary>
    public string? ReserveATable { get; set; }

    /// <summary>
    ///     Online ordering link
    /// </summary>
    public string? OrderOnline { get; set; }

    /// <summary>
    ///     Upcoming events at this place
    /// </summary>
    public List<MapsEvent>? Events { get; set; }

    /// <summary>
    ///     Questions and answers about the place
    /// </summary>
    public List<MapsQA>? QuestionsAndAnswers { get; set; }

    /// <summary>
    ///     Other businesses at this location
    /// </summary>
    public MapsAtThisPlace? AtThisPlace { get; set; }

    /// <summary>
    ///     Ticket and admission information
    /// </summary>
    public List<MapsAdmission>? Admission { get; set; }

    /// <summary>
    ///     Available experiences and tours
    /// </summary>
    public List<MapsExperience>? Experiences { get; set; }

    /// <summary>
    ///     Posts from the business owner
    /// </summary>
    public List<MapsPost>? Posts { get; set; }

    /// <summary>
    ///     Link to fetch all posts via SerpApi
    /// </summary>
    public string? SerpapiPostsLink { get; set; }

    /// <summary>
    ///     Weather information (for cities/locations)
    /// </summary>
    public MapsWeather? Weather { get; set; }

    /// <summary>
    ///     Nearby places at this location
    /// </summary>
    public List<MapsAtLocation>? AtThisLocation { get; set; }
}
