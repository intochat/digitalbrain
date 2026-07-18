using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetUserFeedbackResponse
{
    [JsonPropertyName("title")]
    [DataMember(Name = "title")]
    public string Title { get; set; } = null!;

    [JsonPropertyName("content")]
    [DataMember(Name = "content")]
    public string Content { get; set; } = null!;

    [JsonPropertyName("rating")]
    [DataMember(Name = "rating")]
    public int Rating { get; set; }
    
    [JsonPropertyName("categoryName")]
    [DataMember(Name = "categoryName")]
    public string CategoryName { get; set; } = null!;

    [JsonPropertyName("createdOn")]
    [DataMember(Name = "createdOn")]
    public DateTime CreatedOn { get; set; }
}
