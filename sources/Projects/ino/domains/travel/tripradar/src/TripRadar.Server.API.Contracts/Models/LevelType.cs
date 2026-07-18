using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class LevelType
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("comments")]
    public string? Comments { get; set; }

    [JsonPropertyName("isFastChargeCapable")]
    public bool? IsFastChargeCapable { get; set; }
}
