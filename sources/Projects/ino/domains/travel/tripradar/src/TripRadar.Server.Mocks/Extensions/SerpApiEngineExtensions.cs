using TripRadar.Server.Mocks.Constants;

namespace TripRadar.Server.Mocks.Extensions;

/// <summary>
///     Extension methods for SerpApiEngine enum
/// </summary>
public static class SerpApiEngineExtensions
{
    public static SerpApiEngine ToSerpApiEngine(this string? engineName)
    {
        return engineName?.ToLowerInvariant() switch
        {
            "google_flights" => SerpApiEngine.GoogleFlights,
            "google_hotels" => SerpApiEngine.GoogleHotels,
            "google_events" => SerpApiEngine.GoogleEvents,
            "google_local" => SerpApiEngine.GoogleLocal,
            "google_maps" => SerpApiEngine.GoogleMaps,
            "google_maps_reviews" => SerpApiEngine.PlaceReviews,
            "google_maps_directions" => SerpApiEngine.GoogleMapsDirections,
            "google_travel_explore" => SerpApiEngine.GoogleTravelExplore,
            "tripadvisor" => SerpApiEngine.TripadvisorSearch,
            "tripadvisor_place" => SerpApiEngine.TripadvisorPlace,
            "open_table_reviews" => SerpApiEngine.OpenTableReviews,
            "youtube" => SerpApiEngine.YouTubeSearch,
            "youtube_search" => SerpApiEngine.YouTubeSearch,
            "google_light" => SerpApiEngine.GoogleLightSearch,
            "yelp" => SerpApiEngine.YelpSearch,
            "yelp_search" => SerpApiEngine.YelpSearch,
            "yelp_place" => SerpApiEngine.YelpPlace,
            "yelp_reviews" => SerpApiEngine.YelpReviews,
            _ => SerpApiEngine.Unknown
        };
    }

    public static string ToEngineString(this SerpApiEngine engine)
    {
        return engine switch
        {
            SerpApiEngine.GoogleFlights => "google_flights",
            SerpApiEngine.GoogleHotels => "google_hotels",
            SerpApiEngine.GoogleEvents => "google_events",
            SerpApiEngine.GoogleLocal => "google_local",
            SerpApiEngine.GoogleMaps => "google_maps",
            SerpApiEngine.PlaceReviews => "google_maps_reviews",
            SerpApiEngine.GoogleMapsDirections => "google_maps_directions",
            SerpApiEngine.GoogleTravelExplore => "google_travel_explore",
            SerpApiEngine.TripadvisorSearch => "tripadvisor",
            SerpApiEngine.TripadvisorPlace => "tripadvisor_place",
            SerpApiEngine.OpenTableReviews => "open_table_reviews",
            SerpApiEngine.YouTubeSearch => "youtube",
            SerpApiEngine.GoogleLightSearch => "google_light",
            SerpApiEngine.YelpSearch => "yelp",
            SerpApiEngine.YelpPlace => "yelp_place",
            SerpApiEngine.YelpReviews => "yelp_reviews",
            _ => "unknown"
        };
    }

    public static string GetDescription(this SerpApiEngine engine)
    {
        return engine switch
        {
            SerpApiEngine.GoogleFlights => "Flight search and booking engine",
            SerpApiEngine.GoogleHotels => "Hotel search and booking engine",
            SerpApiEngine.GoogleEvents => "Event discovery and ticket booking engine",
            SerpApiEngine.GoogleLocal => "Local business and places search engine",
            SerpApiEngine.GoogleMaps => "Google Maps place information engine",
            SerpApiEngine.PlaceReviews => "Place reviews search engine",
            SerpApiEngine.GoogleMapsDirections => "Google Maps directions engine",
            SerpApiEngine.GoogleTravelExplore => "Travel destination exploration engine",
            SerpApiEngine.TripadvisorSearch => "TripAdvisor search engine",
            SerpApiEngine.TripadvisorPlace => "TripAdvisor place details engine",
            SerpApiEngine.OpenTableReviews => "OpenTable reviews engine",
            SerpApiEngine.YouTubeSearch => "YouTube search engine",
            SerpApiEngine.GoogleLightSearch => "Google Light search engine",
            SerpApiEngine.YelpSearch => "Yelp search engine",
            SerpApiEngine.YelpPlace => "Yelp place details engine",
            SerpApiEngine.YelpReviews => "Yelp reviews engine",
            SerpApiEngine.Unknown => "Unknown or unsupported search engine",
            _ => "Invalid engine type"
        };
    }
}
