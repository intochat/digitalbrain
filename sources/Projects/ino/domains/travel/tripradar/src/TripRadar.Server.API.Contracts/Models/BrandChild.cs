using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class BrandChild
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("name")] public string? Name { get; set; }
}
