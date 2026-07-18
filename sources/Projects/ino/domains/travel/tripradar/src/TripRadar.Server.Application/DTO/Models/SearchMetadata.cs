using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Convertors;

namespace TripRadar.Server.Application.DTO.Models;

public class SearchMetadata
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

    [JsonPropertyName("google_flights_url")]
    public string? GoogleFlightsUrl { get; set; }

    [JsonPropertyName("google_local_url")]
    public string? GoogleLocalUrl { get; set; }

    [JsonPropertyName("google_maps_url")]
    public string? GoogleMapsUrl { get; set; }

    [JsonPropertyName("google_maps_directions_url")]
    public string? GoogleMapsDirectionsUrl { get; set; }

    [JsonPropertyName("raw_html_file")]
    public string? RawHtmlFile { get; set; }

    [JsonPropertyName("prettify_html_file")]
    public string? PrettifyHtmlFile { get; set; }

    [JsonPropertyName("open_table_reviews_url")]
    public string? OpenTableReviewsUrl { get; set; }

    [JsonPropertyName("yelp_url")]
    public string? YelpUrl { get; set; }

    [JsonPropertyName("yelp_place_url")]
    public string? YelpPlaceUrl { get; set; }

    [JsonPropertyName("yelp_reviews_url")]
    public string? YelpReviewsUrl { get; set; }

    [JsonPropertyName("total_time_taken")]
    [JsonConverter(typeof(FlexibleDoubleConverter))]
    public double TotalTimeTaken { get; set; }
}
