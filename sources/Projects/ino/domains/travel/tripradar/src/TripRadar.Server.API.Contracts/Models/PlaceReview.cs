using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class PlaceReview
{
    [JsonPropertyName("link")] public string? Link { get; set; }

    [JsonPropertyName("position")] public int Position { get; set; }

    [JsonPropertyName("user")] public PlaceReviewsUser? User { get; set; }

    [JsonPropertyName("rating")] public double Rating { get; set; }

    [JsonPropertyName("date")] public string? Date { get; set; }

    [JsonPropertyName("iso_date")] public string? IsoDate { get; set; }

    [JsonPropertyName("iso_date_of_last_edit")]
    public string? IsoDateOfLastEdit { get; set; }

    [JsonPropertyName("snippet")] public string? Snippet { get; set; }

    [JsonPropertyName("extracted_snippet")]
    public PlaceReviewsExtractedSnippet? ExtractedSnippet { get; set; }

    [JsonPropertyName("likes")] public int? Likes { get; set; }

    [JsonPropertyName("images")] public List<string>? Images { get; set; }

    [JsonPropertyName("source")] public string? Source { get; set; }

    [JsonPropertyName("review_id")] public string? ReviewId { get; set; }

    [JsonPropertyName("local_guide")] public bool? LocalGuide { get; set; }

    [JsonPropertyName("details")] public PlaceReviewsDetails? Details { get; set; }

    [JsonPropertyName("response")] public PlaceReviewsOwnerResponse? Response { get; set; }

    [JsonPropertyName("response_from_owner_text")]
    public string? ResponseFromOwnerText { get; set; }

    [JsonPropertyName("response_from_owner_ago")]
    public string? ResponseFromOwnerAgo { get; set; }
}
