using System.Text.Json.Serialization;

namespace TripRadar.Server.Mocks.Responses.SerpApi.Local;

public class GoogleLocalResponse
{
    [JsonPropertyName("search_metadata")] public SearchMetadata? SearchMetadata { get; set; }

    [JsonPropertyName("search_parameters")]
    public SearchParameters? SearchParameters { get; set; }

    [JsonPropertyName("local_results")] public List<LocalResult>? LocalResults { get; set; }

    [JsonPropertyName("place_results")] public PlaceResults? PlaceResults { get; set; }

    [JsonPropertyName("pagination")] public Pagination? Pagination { get; set; }
}

public class SearchMetadata
{
    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("status")] public string? Status { get; set; }

    [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }

    [JsonPropertyName("processed_at")] public string? ProcessedAt { get; set; }

    [JsonPropertyName("google_url")] public string? GoogleUrl { get; set; }

    [JsonPropertyName("raw_html_file")] public string? RawHtmlFile { get; set; }

    [JsonPropertyName("total_time_taken")] public double TotalTimeTaken { get; set; }
}

public class SearchParameters
{
    [JsonPropertyName("engine")] public string? Engine { get; set; }

    [JsonPropertyName("q")] public string? Query { get; set; }

    [JsonPropertyName("location")] public string? Location { get; set; }

    [JsonPropertyName("hl")] public string? Hl { get; set; }

    [JsonPropertyName("gl")] public string? Gl { get; set; }

    [JsonPropertyName("type")] public string? Type { get; set; }
}

public class LocalResult
{
    [JsonPropertyName("position")] public int Position { get; set; }

    [JsonPropertyName("title")] public string? Title { get; set; }

    [JsonPropertyName("place_id")] public string? PlaceId { get; set; }

    [JsonPropertyName("data_id")] public string? DataId { get; set; }

    [JsonPropertyName("gps_coordinates")] public GpsCoordinates? GpsCoordinates { get; set; }

    [JsonPropertyName("rating")] public double Rating { get; set; }

    [JsonPropertyName("reviews")] public int Reviews { get; set; }

    [JsonPropertyName("price")] public string? Price { get; set; }

    [JsonPropertyName("type")] public string? Type { get; set; }

    [JsonPropertyName("types")] public List<string>? Types { get; set; }

    [JsonPropertyName("address")] public string? Address { get; set; }

    [JsonPropertyName("open_state")] public string? OpenState { get; set; }

    [JsonPropertyName("hours")] public string? Hours { get; set; }

    [JsonPropertyName("operating_hours")] public OperatingHours? OperatingHours { get; set; }

    [JsonPropertyName("phone")] public string? Phone { get; set; }

    [JsonPropertyName("website")] public string? Website { get; set; }

    [JsonPropertyName("description")] public string? Description { get; set; }

    [JsonPropertyName("thumbnail")] public string? Thumbnail { get; set; }
}

public class GpsCoordinates
{
    [JsonPropertyName("latitude")] public double Latitude { get; set; }

    [JsonPropertyName("longitude")] public double Longitude { get; set; }
}

public class OperatingHours
{
    [JsonPropertyName("monday")] public string? Monday { get; set; }

    [JsonPropertyName("tuesday")] public string? Tuesday { get; set; }

    [JsonPropertyName("wednesday")] public string? Wednesday { get; set; }

    [JsonPropertyName("thursday")] public string? Thursday { get; set; }

    [JsonPropertyName("friday")] public string? Friday { get; set; }

    [JsonPropertyName("saturday")] public string? Saturday { get; set; }

    [JsonPropertyName("sunday")] public string? Sunday { get; set; }
}

public class PlaceResults
{
    [JsonPropertyName("title")] public string? Title { get; set; }

    [JsonPropertyName("data_id")] public string? DataId { get; set; }

    [JsonPropertyName("gps_coordinates")] public GpsCoordinates? GpsCoordinates { get; set; }

    [JsonPropertyName("place_id")] public string? PlaceId { get; set; }

    [JsonPropertyName("rating")] public double Rating { get; set; }

    [JsonPropertyName("reviews")] public int Reviews { get; set; }

    [JsonPropertyName("price")] public string? Price { get; set; }

    [JsonPropertyName("type")] public string? Type { get; set; }

    [JsonPropertyName("address")] public string? Address { get; set; }

    [JsonPropertyName("open_state")] public string? OpenState { get; set; }

    [JsonPropertyName("operating_hours")] public OperatingHours? OperatingHours { get; set; }

    [JsonPropertyName("phone")] public string? Phone { get; set; }

    [JsonPropertyName("website")] public string? Website { get; set; }

    [JsonPropertyName("description")] public string? Description { get; set; }

    [JsonPropertyName("photos")] public List<Photo>? Photos { get; set; }
}

public class Photo
{
    [JsonPropertyName("image")] public string? Image { get; set; }

    [JsonPropertyName("thumbnail")] public string? Thumbnail { get; set; }
}

public class Pagination
{
    [JsonPropertyName("current")] public int Current { get; set; }

    [JsonPropertyName("next")] public string? Next { get; set; }

    [JsonPropertyName("other_pages")] public object? OtherPages { get; set; }
}
