using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class PlaceReviewsSearchMetadataDTO
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("json_endpoint")]
    public string? JsonEndpoint { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("processed_at")]
    public string? ProcessedAt { get; set; }

    [JsonPropertyName("google_maps_reviews_url")]
    public string? GoogleMapsReviewsUrl { get; set; }

    [JsonPropertyName("google_url")]
    public string? GoogleUrl { get; set; }

    [JsonPropertyName("raw_html_file")]
    public string? RawHtmlFile { get; set; }

    [JsonPropertyName("prettify_html_file")]
    public string? PrettifyHtmlFile { get; set; }

    [JsonPropertyName("total_time_taken")]
    public double TotalTimeTaken { get; set; }
}
