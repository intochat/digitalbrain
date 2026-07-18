using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class Image
{
    [JsonPropertyName("thumbnail")] public string? Thumbnail { get; set; }

    [JsonPropertyName("original_image")] public string? OriginalImage { get; set; }
}
