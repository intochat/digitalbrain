using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsLiveHashDTO
{
    [JsonPropertyName("info")]
    public string? Info { get; set; }

    [JsonPropertyName("time_spent")]
    public string? TimeSpent { get; set; }
}
