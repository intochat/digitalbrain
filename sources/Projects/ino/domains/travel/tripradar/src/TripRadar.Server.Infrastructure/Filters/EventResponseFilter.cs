using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Constants.Events;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.Infrastructure.Filters;

public class EventResponseFilter(ILogger<BaseSearchResponseFilter<GetEventResponseDTO>> logger)
    : BaseSearchResponseFilter<GetEventResponseDTO>(logger)
{
    private static readonly Dictionary<string, string> _eventMappings = new()
    {
        { nameof(EventDTO.Title), SavedEventQueryColumnNameConstants.Title },
        { nameof(EventDTO.Date), SavedEventQueryColumnNameConstants.Date },
        { nameof(EventDTO.Address), SavedEventQueryColumnNameConstants.Address },
        { nameof(EventDTO.Link), SavedEventQueryColumnNameConstants.Link },
        { nameof(EventDTO.Description), SavedEventQueryColumnNameConstants.Description },
        { nameof(EventDTO.TicketInfo), SavedEventQueryColumnNameConstants.TicketInfo },
        { nameof(EventDTO.Venue), SavedEventQueryColumnNameConstants.Venue },
        { nameof(EventDTO.Thumbnail), SavedEventQueryColumnNameConstants.Thumbnail },
        { nameof(EventDTO.EventLocationMap), SavedEventQueryColumnNameConstants.EventLocationMap }
    };

    private static readonly Dictionary<string, string> _eventDateMappings = new()
    {
        { nameof(EventDateDTO.StartDate), SavedEventQueryColumnNameConstants.StartDate },
        { nameof(EventDateDTO.When), SavedEventQueryColumnNameConstants.When }
    };

    private static readonly Dictionary<string, string> _venueMappings = new()
    {
        { nameof(VenueDTO.Name), SavedEventQueryColumnNameConstants.VenueName },
        { nameof(VenueDTO.Rating), SavedEventQueryColumnNameConstants.Rating },
        { nameof(VenueDTO.Reviews), SavedEventQueryColumnNameConstants.Reviews },
        { nameof(VenueDTO.Link), SavedEventQueryColumnNameConstants.Link }
    };

    private static readonly Dictionary<string, string> _ticketInfoMappings = new()
    {
        { nameof(TicketInfoDTO.Source), SavedEventQueryColumnNameConstants.Source },
        { nameof(TicketInfoDTO.Link), SavedEventQueryColumnNameConstants.Link },
        { nameof(TicketInfoDTO.LinkType), SavedEventQueryColumnNameConstants.LinkType }
    };

    private static readonly Dictionary<string, string> _eventLocationMapMappings = new()
    {
        { nameof(EventLocationMapDTO.Image), SavedEventQueryColumnNameConstants.Image },
        { nameof(EventLocationMapDTO.Link), SavedEventQueryColumnNameConstants.Link },
        { nameof(EventLocationMapDTO.SerpapiLink), SavedEventQueryColumnNameConstants.SerpapiLink }
    };

    protected override GetEventResponseDTO FilterResponse(GetEventResponseDTO response, List<string> activeColumns)
    {
        var filteredResponse = new GetEventResponseDTO();

        // Check if root level containers should be included
        if (ShouldIncludeContainer(SavedEventQueryColumnNameConstants.SearchMetadata, activeColumns))
            filteredResponse.SearchMetadata = response.SearchMetadata;

        if (ShouldIncludeContainer(SavedEventQueryColumnNameConstants.SearchParameters, activeColumns))
            filteredResponse.SearchParameters = response.SearchParameters;

        if (ShouldIncludeContainer(SavedEventQueryColumnNameConstants.SearchInformation, activeColumns))
            filteredResponse.SearchInformation = response.SearchInformation;

        if (ShouldIncludeContainer(SavedEventQueryColumnNameConstants.EventsResults, activeColumns) && response.EventsResults != null)
            filteredResponse.EventsResults = FilterEvents(response.EventsResults, activeColumns);

        return filteredResponse;
    }

    private static List<EventDTO> FilterEvents(List<EventDTO> events, List<string> activeColumns)
    {
        return events.Select(eventItem =>
        {
            var filteredEvent = CreateFilteredInstance(eventItem, activeColumns, _eventMappings);

            if (IsColumnActive(SavedEventQueryColumnNameConstants.Date, activeColumns))
                filteredEvent.Date = CreateFilteredInstance(eventItem.Date, activeColumns, _eventDateMappings);

            if (IsColumnActive(SavedEventQueryColumnNameConstants.Venue, activeColumns) && eventItem.Venue != null)
                filteredEvent.Venue = CreateFilteredInstance(eventItem.Venue, activeColumns, _venueMappings);

            if (IsColumnActive(SavedEventQueryColumnNameConstants.TicketInfo, activeColumns))
                filteredEvent.TicketInfo = FilterTicketInfo(eventItem.TicketInfo, activeColumns);

            if (IsColumnActive(SavedEventQueryColumnNameConstants.EventLocationMap, activeColumns) && eventItem.EventLocationMap != null)
                filteredEvent.EventLocationMap = CreateFilteredInstance(eventItem.EventLocationMap, activeColumns, _eventLocationMapMappings);

            return filteredEvent;
        }).ToList();
    }

    private static List<TicketInfoDTO> FilterTicketInfo(List<TicketInfoDTO> ticketInfos, List<string> activeColumns)
    {
        return ticketInfos.Select(ticket => CreateFilteredInstance(ticket, activeColumns, _ticketInfoMappings))
            .ToList();
    }

    private static bool ShouldIncludeContainer(string containerName, List<string> activeColumns)
    {
        if (IsColumnActive(containerName, activeColumns))
        {
            return true;
        }

        return containerName switch
        {
            SavedEventQueryColumnNameConstants.EventsResults => activeColumns.Any(IsEventResultColumn),
            SavedEventQueryColumnNameConstants.SearchMetadata => activeColumns.Any(IsSearchMetadataColumn),
            SavedEventQueryColumnNameConstants.SearchParameters => activeColumns.Any(IsSearchParametersColumn),
            SavedEventQueryColumnNameConstants.SearchInformation => activeColumns.Any(IsSearchInformationColumn),
            _ => false
        };
    }

    private static bool IsEventResultColumn(string columnName) => columnName is SavedEventQueryColumnNameConstants.Title or SavedEventQueryColumnNameConstants.Date or SavedEventQueryColumnNameConstants.Address or SavedEventQueryColumnNameConstants.Link or SavedEventQueryColumnNameConstants.Description or SavedEventQueryColumnNameConstants.TicketInfo or SavedEventQueryColumnNameConstants.Venue or SavedEventQueryColumnNameConstants.Thumbnail or SavedEventQueryColumnNameConstants.EventLocationMap or SavedEventQueryColumnNameConstants.StartDate or SavedEventQueryColumnNameConstants.When or SavedEventQueryColumnNameConstants.VenueName or SavedEventQueryColumnNameConstants.Rating or SavedEventQueryColumnNameConstants.Reviews or SavedEventQueryColumnNameConstants.Source or SavedEventQueryColumnNameConstants.LinkType or SavedEventQueryColumnNameConstants.Image or SavedEventQueryColumnNameConstants.SerpapiLink;

    private static bool IsSearchMetadataColumn(string columnName) => columnName is SavedEventQueryColumnNameConstants.Id or SavedEventQueryColumnNameConstants.Status or SavedEventQueryColumnNameConstants.JsonEndpoint or SavedEventQueryColumnNameConstants.CreatedAt or SavedEventQueryColumnNameConstants.ProcessedAt or SavedEventQueryColumnNameConstants.GoogleEventsUrl or SavedEventQueryColumnNameConstants.RawHtmlFile or SavedEventQueryColumnNameConstants.TotalTimeTaken;

    private static bool IsSearchParametersColumn(string columnName) => columnName is SavedEventQueryColumnNameConstants.Query or SavedEventQueryColumnNameConstants.Engine or SavedEventQueryColumnNameConstants.Htichips or SavedEventQueryColumnNameConstants.LanguageCode or SavedEventQueryColumnNameConstants.CountryCode or SavedEventQueryColumnNameConstants.GoogleDomain or SavedEventQueryColumnNameConstants.Uule;

    private static bool IsSearchInformationColumn(string columnName) => columnName == SavedEventQueryColumnNameConstants.EventsResultsState;
}
