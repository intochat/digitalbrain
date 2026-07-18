using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class SearchInformation
{
    [JsonPropertyName("total_results")]
    public int TotalResults { get; set; }
}
