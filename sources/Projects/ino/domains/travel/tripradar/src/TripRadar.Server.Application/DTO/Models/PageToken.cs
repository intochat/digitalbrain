using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class PageToken
{
    [JsonPropertyName("nextPageToken")]
    public string? NextPageToken { get; set; }
}
