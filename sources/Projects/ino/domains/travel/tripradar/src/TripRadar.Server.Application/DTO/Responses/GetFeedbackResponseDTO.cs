using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Responses;

public class GetFeedbackResponseDTO
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
    [JsonPropertyName("title")]
    public string Title { get; set; } = null!;
    [JsonPropertyName("content")]
    public string Content { get; set; } = null!;
    [JsonPropertyName("rating")]
    public int Rating { get; set; }
    [JsonPropertyName("category")]
    public string Category { get; set; } = null!;
    [JsonPropertyName("createdOn")]
    public DateTime CreatedOn { get; set; }
    [JsonPropertyName("updatedOn")]
    public DateTime? UpdatedOn { get; set; }
}
