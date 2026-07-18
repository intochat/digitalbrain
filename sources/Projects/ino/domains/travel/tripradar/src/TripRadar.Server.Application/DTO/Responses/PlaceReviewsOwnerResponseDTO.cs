using System.Text.Json.Serialization;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.DTO.Responses;

public class PlaceReviewsOwnerResponseDTO
{
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("isoDate")]
    public string? IsoDate { get; set; }

    [JsonPropertyName("iso_date_of_last_edit")]
    public string? IsoDateOfLastEdit { get; set; }

    [JsonPropertyName("snippet")]
    public string? Snippet { get; set; }

    [JsonPropertyName("extracted_snippet")]
    public PlaceReviewsExtractedSnippetDTO? ExtractedSnippet { get; set; }
}
