namespace TripRadar.Server.Mocks.Constants;

/// <summary>
///     Represents the supported SerpApi search engines for mock responses
/// </summary>
public enum SerpApiEngine
{
    /// <summary>
    ///     Unknown or unsupported engine type
    /// </summary>
    Unknown = 0,

    /// <summary>
    ///     Google Flights search engine for flight booking searches
    /// </summary>
    GoogleFlights = 1,

    /// <summary>
    ///     Google Hotels search engine for hotel booking searches
    /// </summary>
    GoogleHotels = 2,

    /// <summary>
    ///     Google Events search engine for event discovery
    /// </summary>
    GoogleEvents = 3,

    /// <summary>
    ///     Google Local search engine for local business searches
    /// </summary>
    GoogleLocal = 4,

    /// <summary>
    ///     Place Reviews search engine for place reviews
    /// </summary>
    PlaceReviews = 5,

    /// <summary>
    ///     Google Maps search engine for maps
    /// </summary>
    GoogleMaps = 6,

    /// <summary>
    ///     Google Travel Explore search engine for destination exploration
    /// </summary>
    GoogleTravelExplore = 7,

    /// <summary>
    ///     Tripadvisor search engine for TripAdvisor searches
    /// </summary>
    TripadvisorSearch = 8,

    /// <summary>
    ///     Tripadvisor place details engine
    /// </summary>
    TripadvisorPlace = 9,

    /// <summary>
    ///     Yelp search engine for listing places
    /// </summary>
    YelpSearch = 10,

    /// <summary>
    ///     Yelp place details engine
    /// </summary>
    YelpPlace = 11,

    /// <summary>
    ///     Yelp reviews engine
    /// </summary>
    YelpReviews = 12,

    /// <summary>
    ///     Google Maps directions engine
    /// </summary>
    GoogleMapsDirections = 13,

    /// <summary>
    ///     OpenTable reviews engine
    /// </summary>
    OpenTableReviews = 14,

    /// <summary>
    ///     YouTube search engine
    /// </summary>
    YouTubeSearch = 15,

    /// <summary>
    ///     Google Light search engine
    /// </summary>
    GoogleLightSearch = 16
}
