namespace TripRadar.Server.Application.Constants.Events;

public static class SavedEventQueryColumnNameConstants
{
    // Root level properties
    public const string SearchMetadata = "search_metadata";
    public const string SearchParameters = "search_parameters";
    public const string SearchInformation = "search_information";
    public const string EventsResults = "events_results";
    public const string SerpapiPagination = "serpapi_pagination";

    // Search metadata properties
    public const string Id = "id";
    public const string Status = "status";
    public const string JsonEndpoint = "json_endpoint";
    public const string CreatedAt = "created_at";
    public const string ProcessedAt = "processed_at";
    public const string GoogleEventsUrl = "google_events_url";
    public const string RawHtmlFile = "raw_html_file";
    public const string TotalTimeTaken = "total_time_taken";

    // Search parameters properties
    public const string Query = "q";
    public const string Engine = "engine";
    public const string Htichips = "htichips";
    public const string LanguageCode = "hl";
    public const string CountryCode = "gl";
    public const string GoogleDomain = "google_domain";
    public const string Uule = "uule";

    // Search information properties
    public const string EventsResultsState = "events_results_state";

    // Event properties
    public const string Title = "title";
    public const string Date = "date";
    public const string Address = "address";
    public const string Link = "link";
    public const string Description = "description";
    public const string TicketInfo = "ticket_info";
    public const string Venue = "venue";
    public const string Thumbnail = "thumbnail";
    public const string EventLocationMap = "event_location_map";

    // Date properties (nested under date object)
    public const string StartDate = "start_date";
    public const string When = "when";

    // Venue properties (nested under venue object)
    public const string VenueName = "name";
    public const string Rating = "rating";
    public const string Reviews = "reviews";

    // Ticket info properties (array of objects)
    public const string Source = "source";
    public const string LinkType = "link_type";

    // Event location map properties (nested under event_location_map object)
    public const string Image = "image";
    public const string SerpapiLink = "serpapi_link";

    // Pagination properties
    public const string NextPage = "next";
    public const string Current = "current";
}
