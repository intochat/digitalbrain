using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Enums;

public class ServiceType(int id, string name, string description = "") : Enumeration(id, name)
{
    public string Description { get; } = description;

    public static readonly ServiceType Event = new(1, nameof(Event), "Search for events");
    public static readonly ServiceType Flight = new(2, nameof(Flight), "Search for flights");
    public static readonly ServiceType Hotel = new(3, nameof(Hotel), "Search for hotels");
    public static readonly ServiceType LocalPlaces = new(4, nameof(LocalPlaces), "Search for local places");
    public static readonly ServiceType Maps = new(5, nameof(Maps), "Search maps");
    public static readonly ServiceType PlaceReview = new(6, nameof(PlaceReview), "Get place reviews");
    public static readonly ServiceType FlightExplore = new(7, nameof(FlightExplore), "Explore flight destinations");
    public static readonly ServiceType TripAdvisorSearch = new(8, nameof(TripAdvisorSearch), "TripAdvisor search");
    public static readonly ServiceType TripAdvisorPlace = new(9, nameof(TripAdvisorPlace), "TripAdvisor place details");
    public static readonly ServiceType OpenTableReview = new(10, nameof(OpenTableReview), "OpenTable reviews");
    public static readonly ServiceType YouTubeSearch = new(11, nameof(YouTubeSearch), "Search YouTube videos");
    public static readonly ServiceType YelpSearch = new(12, nameof(YelpSearch), "Yelp search");
    public static readonly ServiceType YelpPlace = new(13, nameof(YelpPlace), "Yelp place details");
    public static readonly ServiceType YelpReviews = new(14, nameof(YelpReviews), "Yelp reviews");
    public static readonly ServiceType YelpPlaceFullMenu = new(15, nameof(YelpPlaceFullMenu), "Yelp place full menu");
    public static readonly ServiceType MapsDirections = new(16, nameof(MapsDirections), "Google Maps directions");
    public static readonly ServiceType MapsPlaceResults = new(17, nameof(MapsPlaceResults), "Google Maps place results");
    public static readonly ServiceType GoogleLightSearch = new(18, nameof(GoogleLightSearch), "Google Light search");
    public static readonly ServiceType FlightPriceCalendar = new(20, nameof(FlightPriceCalendar), "Flight price calendar lookup");

    public static IReadOnlyList<ServiceType> GetAllServices() =>
    [
        Flight,
        Hotel,
        Event,
        LocalPlaces,
        Maps,
        PlaceReview,
        FlightExplore,
        TripAdvisorSearch,
        TripAdvisorPlace,
        OpenTableReview,
        YouTubeSearch,
        YelpSearch,
        YelpPlace,
        YelpReviews,
        YelpPlaceFullMenu,
        MapsDirections,
        MapsPlaceResults,
        GoogleLightSearch,
        FlightPriceCalendar
    ];

    public static IReadOnlyList<ServiceType> GetActivePreferenceServices() =>
    [
        Flight,
        Hotel
    ];
}
