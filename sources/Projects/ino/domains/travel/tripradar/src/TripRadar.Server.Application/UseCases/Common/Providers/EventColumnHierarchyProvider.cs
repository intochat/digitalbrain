using TripRadar.Server.Application.Constants.Events;

namespace TripRadar.Server.Application.UseCases.Common.Providers;

public class EventColumnHierarchyProvider : ColumnHierarchyProvider
{
    protected override Dictionary<string, string?> ColumnHierarchies => new()
    {
        // Root level properties
        { SavedEventQueryColumnNameConstants.SearchMetadata, null },
        { SavedEventQueryColumnNameConstants.SearchParameters, null },
        { SavedEventQueryColumnNameConstants.SearchInformation, null },
        { SavedEventQueryColumnNameConstants.EventsResults, null },
        { SavedEventQueryColumnNameConstants.SerpapiPagination, null },

        // Search metadata properties (nested under search_metadata)
        { SavedEventQueryColumnNameConstants.Id, SavedEventQueryColumnNameConstants.SearchMetadata },
        { SavedEventQueryColumnNameConstants.Status, SavedEventQueryColumnNameConstants.SearchMetadata },
        { SavedEventQueryColumnNameConstants.JsonEndpoint, SavedEventQueryColumnNameConstants.SearchMetadata },
        { SavedEventQueryColumnNameConstants.CreatedAt, SavedEventQueryColumnNameConstants.SearchMetadata },
        { SavedEventQueryColumnNameConstants.ProcessedAt, SavedEventQueryColumnNameConstants.SearchMetadata },
        { SavedEventQueryColumnNameConstants.GoogleEventsUrl, SavedEventQueryColumnNameConstants.SearchMetadata },
        { SavedEventQueryColumnNameConstants.RawHtmlFile, SavedEventQueryColumnNameConstants.SearchMetadata },
        { SavedEventQueryColumnNameConstants.TotalTimeTaken, SavedEventQueryColumnNameConstants.SearchMetadata },

        // Search parameters properties (nested under search_parameters)
        { SavedEventQueryColumnNameConstants.Query, SavedEventQueryColumnNameConstants.SearchParameters },
        { SavedEventQueryColumnNameConstants.Engine, SavedEventQueryColumnNameConstants.SearchParameters },
        { SavedEventQueryColumnNameConstants.Htichips, SavedEventQueryColumnNameConstants.SearchParameters },
        { SavedEventQueryColumnNameConstants.LanguageCode, SavedEventQueryColumnNameConstants.SearchParameters },
        { SavedEventQueryColumnNameConstants.CountryCode, SavedEventQueryColumnNameConstants.SearchParameters },
        { SavedEventQueryColumnNameConstants.GoogleDomain, SavedEventQueryColumnNameConstants.SearchParameters },
        { SavedEventQueryColumnNameConstants.Uule, SavedEventQueryColumnNameConstants.SearchParameters },

        // Search information properties (nested under search_information)
        { SavedEventQueryColumnNameConstants.EventsResultsState, SavedEventQueryColumnNameConstants.SearchInformation },

        // Event properties (nested under events_results array)
        { SavedEventQueryColumnNameConstants.Title, SavedEventQueryColumnNameConstants.EventsResults },
        { SavedEventQueryColumnNameConstants.Date, SavedEventQueryColumnNameConstants.EventsResults },
        { SavedEventQueryColumnNameConstants.Address, SavedEventQueryColumnNameConstants.EventsResults },
        { SavedEventQueryColumnNameConstants.Link, SavedEventQueryColumnNameConstants.EventsResults },
        { SavedEventQueryColumnNameConstants.Description, SavedEventQueryColumnNameConstants.EventsResults },
        { SavedEventQueryColumnNameConstants.TicketInfo, SavedEventQueryColumnNameConstants.EventsResults },
        { SavedEventQueryColumnNameConstants.Venue, SavedEventQueryColumnNameConstants.EventsResults },
        { SavedEventQueryColumnNameConstants.Thumbnail, SavedEventQueryColumnNameConstants.EventsResults },
        { SavedEventQueryColumnNameConstants.EventLocationMap, SavedEventQueryColumnNameConstants.EventsResults },

        // Date properties (nested under date object within events_results)
        { SavedEventQueryColumnNameConstants.StartDate, SavedEventQueryColumnNameConstants.Date },
        { SavedEventQueryColumnNameConstants.When, SavedEventQueryColumnNameConstants.Date },

        // Venue properties (nested under venue object within events_results)
        { SavedEventQueryColumnNameConstants.VenueName, SavedEventQueryColumnNameConstants.Venue },
        { SavedEventQueryColumnNameConstants.Rating, SavedEventQueryColumnNameConstants.Venue },
        { SavedEventQueryColumnNameConstants.Reviews, SavedEventQueryColumnNameConstants.Venue },

        // Ticket info properties (nested under ticket_info array within events_results)
        { SavedEventQueryColumnNameConstants.Source, SavedEventQueryColumnNameConstants.TicketInfo },
        { SavedEventQueryColumnNameConstants.LinkType, SavedEventQueryColumnNameConstants.TicketInfo },

        // Event location map properties (nested under event_location_map object within events_results)
        { SavedEventQueryColumnNameConstants.Image, SavedEventQueryColumnNameConstants.EventLocationMap },
        { SavedEventQueryColumnNameConstants.SerpapiLink, SavedEventQueryColumnNameConstants.EventLocationMap },

        // Pagination properties (nested under serpapi_pagination)
        { SavedEventQueryColumnNameConstants.NextPage, SavedEventQueryColumnNameConstants.SerpapiPagination },
        { SavedEventQueryColumnNameConstants.Current, SavedEventQueryColumnNameConstants.SerpapiPagination }
    };

    protected override HashSet<string?> ValidColumns => [..ColumnHierarchies.Keys.Concat(ColumnHierarchies.Values)];
}
