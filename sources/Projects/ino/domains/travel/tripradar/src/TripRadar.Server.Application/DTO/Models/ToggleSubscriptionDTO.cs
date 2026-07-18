using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class ToggleSubscriptionDTO
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}
