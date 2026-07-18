using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsPopularTimesDTO
{
    [JsonPropertyName("graph_results")]
    public Dictionary<string, List<MapsPopularTimeSlotDTO>>? GraphResults { get; set; }

    [JsonPropertyName("live_hash")]
    public MapsLiveHashDTO? LiveHash { get; set; }
}
