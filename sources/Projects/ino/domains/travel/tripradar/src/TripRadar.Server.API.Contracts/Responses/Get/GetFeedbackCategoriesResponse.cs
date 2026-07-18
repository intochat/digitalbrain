using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetFeedbackCategoriesResponse
{
    [JsonPropertyName("categories")]
    public List<FeedbackCategory> Categories { get; set; } = [];
}
