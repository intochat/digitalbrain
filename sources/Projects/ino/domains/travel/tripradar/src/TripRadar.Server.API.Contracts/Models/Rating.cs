using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class Rating
{
    [JsonPropertyName("stars")] public int Stars { get; set; }

    [JsonPropertyName("count")] public int Count { get; set; }
}
