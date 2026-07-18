using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Enums;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public class CreateFeedbackRequest
{
    [JsonPropertyName("title")]
    [DataMember(Name = "title")]
    [Required(AllowEmptyStrings = false)]
    [StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = null!;

    [JsonPropertyName("content")]
    [DataMember(Name = "content")]
    [Required(AllowEmptyStrings = false)]
    [StringLength(2000, MinimumLength = 1)]
    public string Content { get; set; } = null!;

    [JsonPropertyName("rating")]
    [DataMember(Name = "rating")]
    [Range(1, 5)]
    public int Rating { get; set; }

    [JsonPropertyName("feedbackCategoryType")]
    [DataMember(Name = "feedbackCategoryType")]
    [Required]
    public FeedbackCategoryType FeedbackCategoryType { get; set; }
}
