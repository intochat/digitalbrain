using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class FeedbackCategory
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;
}
