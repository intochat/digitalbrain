using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class PlaceReviewsOwnerResponse
{
    [JsonPropertyName("date")] public string? Date { get; set; }

    [JsonPropertyName("iso_date")] public string? IsoDate { get; set; }

    [JsonPropertyName("iso_date_of_last_edit")]
    public string? IsoDateOfLastEdit { get; set; }

    [JsonPropertyName("snippet")] public string? Snippet { get; set; }

    [JsonPropertyName("extracted_snippet")]
    public PlaceReviewsExtractedSnippet? ExtractedSnippet { get; set; }
}
