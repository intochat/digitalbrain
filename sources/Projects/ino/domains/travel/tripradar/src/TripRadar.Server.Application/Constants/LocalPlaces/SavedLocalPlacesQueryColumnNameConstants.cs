namespace TripRadar.Server.Application.Constants.LocalPlaces;

public static class SavedLocalPlacesQueryColumnNameConstants
{
    // Root level response properties
    public const string SearchMetadata = "search_metadata";
    public const string SearchParameters = "search_parameters";
    public const string AdResults = "ads_results";
    public const string LocalMap = "local_map";
    public const string LocalResults = "local_results";
    public const string DiscoverMorePlaces = "discover_more_places";
    public const string Pagination = "pagination";
    public const string SerpApiPagination = "serpapi_pagination";

    // Search metadata properties
    public const string Id = "id";
    public const string Status = "status";
    public const string JsonEndpoint = "json_endpoint";
    public const string CreatedAt = "created_at";
    public const string ProcessedAt = "processed_at";
    public const string RawHtmlFile = "raw_html_file";
    public const string TotalTimeTaken = "total_time_taken";

    // Search parameters properties
    public const string Engine = "engine";
    public const string Query = "q";
    public const string LocationRequested = "location_requested";
    public const string LocationUsed = "location_used";
    public const string GoogleDomain = "google_domain";
    public const string LanguageCode = "hl";
    public const string CountryCode = "gl";
    public const string Device = "device";
    public const string Uule = "uule";

    // Local map properties
    public const string Image = "image";

    // Local result properties
    public const string Position = "position";
    public const string Title = "title";
    public const string Rating = "rating";
    public const string ReviewsOriginal = "reviews_original";
    public const string Reviews = "reviews";
    public const string Price = "price";
    public const string Type = "type";
    public const string Address = "address";
    public const string Description = "description";
    public const string PlaceId = "place_id";
    public const string PlaceIdSearch = "place_id_search";
    public const string ProviderId = "provider_id";
    public const string Lsig = "lsig";
    public const string Thumbnail = "thumbnail";
    public const string Images = "images";
    public const string GpsCoordinates = "gps_coordinates";
    public const string ServiceOptions = "service_options";
    public const string Phone = "phone";
    public const string Hours = "hours";
    public const string Extensions = "extensions";
    public const string Links = "links";

    // GPS coordinates properties
    public const string Latitude = "latitude";
    public const string Longitude = "longitude";

    // Service options properties
    public const string DineIn = "dine_in";
    public const string Takeout = "takeout";
    public const string Delivery = "delivery";
    public const string NoDelivery = "no_delivery";
    public const string InStorePickup = "in_store_pickup";
    public const string InStoreShopping = "in_store_shopping";
    public const string CurbsidePickup = "curbside_pickup";
    public const string NoContactDelivery = "no_contact_delivery";
    public const string Reservable = "reservable";
    public const string WheelchairAccessible = "wheelchair_accessible";

    // Place links properties
    public const string PhoneLink = "phone";
    public const string Directions = "directions";
    public const string Website = "website";
    public const string Order = "order";

    // Advertisement result properties (same as local results but with additional fields)
    public const string AdTitle = "ad_title";
    public const string DisplayedLink = "displayed_link";

    // Discover more places properties
    public const string Places = "places";
    public const string Link = "link";
    public const string SerpApiLink = "serpapi_link";

    // Pagination properties
    public const string Current = "current";
    public const string Next = "next";
    public const string OtherPages = "other_pages";
    public const string NextLink = "next_link";
}
