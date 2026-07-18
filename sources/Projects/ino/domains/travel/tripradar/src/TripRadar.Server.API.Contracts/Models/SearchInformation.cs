using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class SearchInformation
{
    [JsonPropertyName("total_results")] public int TotalResults { get; set; }
}
