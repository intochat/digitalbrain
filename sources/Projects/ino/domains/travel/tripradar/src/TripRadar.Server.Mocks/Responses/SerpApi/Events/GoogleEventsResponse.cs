using System.Text.Json.Serialization;

namespace TripRadar.Server.Mocks.Responses.SerpApi.Events;

public class GoogleEventsResponse
{
    [JsonPropertyName("search_metadata")] public SearchMetadata? SearchMetadata { get; set; }

    [JsonPropertyName("search_parameters")]
    public SearchParameters? SearchParameters { get; set; }

    [JsonPropertyName("events_results")] public List<EventResult>? EventsResults { get; set; }

    [JsonPropertyName("pagination")] public Pagination? Pagination { get; set; }
}

public class SearchMetadata
{
    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("status")] public string? Status { get; set; }

    [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }

    [JsonPropertyName("processed_at")] public string? ProcessedAt { get; set; }

    [JsonPropertyName("google_events_url")]
    public string? GoogleEventsUrl { get; set; }

    [JsonPropertyName("raw_html_file")] public string? RawHtmlFile { get; set; }

    [JsonPropertyName("total_time_taken")] public double TotalTimeTaken { get; set; }
}

public class SearchParameters
{
    [JsonPropertyName("engine")] public string? Engine { get; set; }

    [JsonPropertyName("q")] public string? Query { get; set; }

    [JsonPropertyName("location")] public string? Location { get; set; }

    [JsonPropertyName("date")] public string? Date { get; set; }

    [JsonPropertyName("hl")] public string? Hl { get; set; }

    [JsonPropertyName("gl")] public string? Gl { get; set; }
}

public class EventResult
{
    [JsonPropertyName("position")] public int Position { get; set; }

    [JsonPropertyName("title")] public string? Title { get; set; }

    [JsonPropertyName("date")] public EventDate? Date { get; set; }

    [JsonPropertyName("address")] public string? Address { get; set; }

    [JsonPropertyName("venue")] public Venue? Venue { get; set; }

    [JsonPropertyName("description")] public string? Description { get; set; }

    [JsonPropertyName("ticket_info")] public TicketInfo? TicketInfo { get; set; }

    [JsonPropertyName("thumbnail")] public string? Thumbnail { get; set; }

    [JsonPropertyName("image")] public string? Image { get; set; }
}

public class EventDate
{
    [JsonPropertyName("start_date")] public string? StartDate { get; set; }

    [JsonPropertyName("when")] public string? When { get; set; }
}

public class Venue
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("link")] public string? Link { get; set; }
}

public class TicketInfo
{
    [JsonPropertyName("price")] public string? Price { get; set; }

    [JsonPropertyName("link")] public string? Link { get; set; }
}

public class Pagination
{
    [JsonPropertyName("current")] public int Current { get; set; }

    [JsonPropertyName("next")] public string? Next { get; set; }

    [JsonPropertyName("other_pages")] public object? OtherPages { get; set; }
}
