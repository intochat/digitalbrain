using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class Brand
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("children")] public List<BrandChild>? Children { get; set; }
}
