using System.Text.Json;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.Mocks.Builders;

public class SerpApiGoogleEventsResponseBuilder
{
    private readonly ILogger<SerpApiGoogleEventsResponseBuilder> _logger;
    private readonly Dictionary<string, GetEventResponseDTO> _mockEventsData = new();

    public SerpApiGoogleEventsResponseBuilder(ILogger<SerpApiGoogleEventsResponseBuilder> logger)
    {
        _logger = logger;
        InitializeMockData();
    }

    public GetEventResponseDTO GetEventsData(string query, string location, string date)
    {
        var cacheKey = $"q={query}&location={location}&date={date}";

        if (_mockEventsData.TryGetValue(cacheKey, out var mockData))
        {
            return mockData;
        }

        var locationKey = $"location={location}";
        if (_mockEventsData.TryGetValue(locationKey, out var locationEventsData))
        {
            var adjustedData = ModifyDates(locationEventsData, date);
            _mockEventsData[cacheKey] = adjustedData;

            _logger.LogInformation("Returning adjusted mock events data for location {Location}", location);
            return adjustedData;
        }

        _logger.LogInformation("Creating new mock events data for query {Query} in {Location}", query, location);
        var defaultData = CreateMockEventsData(query, location, date);
        _mockEventsData[cacheKey] = defaultData;
        AddMockLocation(location, defaultData);

        return defaultData;
    }

    private void AddMockLocation(string location, GetEventResponseDTO getEventResponseDto)
    {
        _mockEventsData[$"location={location}"] = getEventResponseDto;
    }

    private static GetEventResponseDTO ModifyDates(GetEventResponseDTO source, string date)
    {
        var clone = JsonSerializer.Deserialize<GetEventResponseDTO>(JsonSerializer.Serialize(source));
        if (clone == null)
        {
            return source;
        }

        if (clone.SearchParameters != null)
        {
            clone.SearchParameters.Query = $"events in {date}";
        }

        if (clone.EventsResults == null)
        {
            return clone;
        }

        foreach (var eventItem in clone.EventsResults)
        {
            eventItem.Date.StartDate = date;
            eventItem.Date.When = DateTime.Parse(date).ToString("MMM dd, yyyy");
        }

        return clone;
    }

    private GetEventResponseDTO CreateMockEventsData(string query, string location, string date)
    {
        var searchMetadata = new SearchMetadata
        {
            Id = Guid.NewGuid().ToString(),
            Status = "Success",
            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss UTC"),
            TotalTimeTaken = 2.45
        };

        var searchParameters = new EventSearchParametersDTO { Engine = "google_events", Query = query };

        var searchInformation = new EventSearchInformation { EventsResultsState = "Results for exact spelling" };

        var eventsResults = GenerateEventResults(location, date);

        return new GetEventResponseDTO
        {
            SearchMetadata = searchMetadata,
            SearchParameters = searchParameters,
            SearchInformation = searchInformation,
            EventsResults = eventsResults
        };
    }

    private static List<EventDTO> GenerateEventResults(string location, string date)
    {
        var random = new Random();
        var eventTypes = new[] { "Concert", "Theater", "Sports", "Festival", "Conference", "Workshop", "Exhibition" };
        var venues = new[] { "Convention Center", "Stadium", "Theater", "Arena", "Park", "Gallery", "Hotel" };

        return Enumerable.Range(1, random.Next(5, 15))
            .Select(i => new EventDTO
            {
                Title = $"{eventTypes[random.Next(eventTypes.Length)]} Event {i}",
                Date = new EventDateDTO { StartDate = date, When = DateTime.Parse(date).ToString("MMM dd, yyyy") },
                Address = [$"{random.Next(100, 9999)} Main St", $"{location}", "USA"],
                Link = $"https://example.com/event/{i}",
                Description =
                    $"Join us for an amazing {eventTypes[random.Next(eventTypes.Length)].ToLower()} experience in {location}.",
                TicketInfo =
                [
                    new TicketInfoDTO { Source = "Ticketmaster", Link = $"https://example.com/tickets/{i}", LinkType = "more_info" }
                ],
                Venue = new VenueDTO
                {
                    Name = $"{location} {venues[random.Next(venues.Length)]}",
                    Rating = Math.Round(random.NextDouble() * 2 + 3, 1),
                    Reviews = random.Next(50, 500),
                    Link = $"https://example.com/venue/{i}"
                },
                Thumbnail = $"https://via.placeholder.com/150x150?text=Event+{i}",
                EventLocationMap = new EventLocationMapDTO
                {
                    Image = $"https://via.placeholder.com/300x200?text=Map+{i}",
                    Link = $"https://maps.google.com/venue/{i}",
                    SerpapiLink = $"https://serpapi.com/search?location={i}"
                }
            })
            .ToList();
    }

    private void InitializeMockData()
    {
        _mockEventsData["location=New York"] =
            CreateMockEventsData("events", "New York", DateTime.Now.ToString("yyyy-MM-dd"));
        _mockEventsData["location=Los Angeles"] =
            CreateMockEventsData("events", "Los Angeles", DateTime.Now.ToString("yyyy-MM-dd"));
        _mockEventsData["location=Chicago"] =
            CreateMockEventsData("events", "Chicago", DateTime.Now.ToString("yyyy-MM-dd"));
    }
}
