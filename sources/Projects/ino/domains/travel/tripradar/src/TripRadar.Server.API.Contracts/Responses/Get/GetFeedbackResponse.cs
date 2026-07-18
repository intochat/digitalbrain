using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetFeedbackResponse
{
    [JsonPropertyName("id")]
    [DataMember(Name = "id")]
    public long Id { get; set; }

    [JsonPropertyName("title")]
    [DataMember(Name = "title")]
    public string Title { get; set; } = null!;

    [JsonPropertyName("content")]
    [DataMember(Name = "content")]
    public string Content { get; set; } = null!;

    [JsonPropertyName("rating")]
    [DataMember(Name = "rating")]
    public int Rating { get; set; }

    [JsonPropertyName("category")]
    [DataMember(Name = "category")]
    public string Category { get; set; } = null!;

    [JsonPropertyName("createdOn")]
    [DataMember(Name = "createdOn")]
    public DateTime CreatedOn { get; set; }

    [JsonPropertyName("updatedOn")]
    [DataMember(Name = "updatedOn")]
    public DateTime? UpdatedOn { get; set; }
}
