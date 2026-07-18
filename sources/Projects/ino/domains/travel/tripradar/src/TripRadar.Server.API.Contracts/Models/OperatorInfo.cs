using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class OperatorInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("websiteUrl")]
    public string? WebsiteUrl { get; set; }

    [JsonPropertyName("comments")]
    public string? Comments { get; set; }

    [JsonPropertyName("phonePrimaryContact")]
    public string? PhonePrimaryContact { get; set; }

    [JsonPropertyName("contactEmail")]
    public string? ContactEmail { get; set; }

    [JsonPropertyName("isPrivateIndividual")]
    public bool? IsPrivateIndividual { get; set; }
}
